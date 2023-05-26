#region License
/* 
 * Copyright (C) 1999-2023 John Källén.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2, or (at your option)
 * any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; see the file COPYING.  If not, write to
 * the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA.
 */
#endregion

using Reko.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Reko.Core.Hll.C
{
    /// <summary>
    /// Symbol table for the C parser.
    /// </summary>
    public class SymbolTable 
    {
        private readonly IPlatform platform;
        private readonly int pointerSize;

        public SymbolTable(IPlatform platform) : this(
            platform,
            new Dictionary<string, PrimitiveType_v1>(),
            new Dictionary<string, SerializedType>())
        {
        }

        public SymbolTable(IPlatform platform, int pointerSize) : this(
            platform,
            new Dictionary<string, PrimitiveType_v1>(),
            new Dictionary<string, SerializedType>())
        {
            this.pointerSize = pointerSize;
        }

        public SymbolTable(
            IPlatform platform,
            Dictionary<string, PrimitiveType_v1> primitiveTypes,
            Dictionary<string, SerializedType> namedTypes)
        {
            this.platform = platform;
            this.pointerSize = platform.PointerType.Size;

            this.Types = new List<SerializedType>();
            this.StructsSeen = new Dictionary<string, StructType_v1>();
            this.UnionsSeen = new Dictionary<string, UnionType_v1>();
            this.EnumsSeen = new Dictionary<string, SerializedEnumType>();
            this.Constants = new Dictionary<string, int>();
            this.Procedures = new List<ProcedureBase_v1>();
            this.Variables = new List<GlobalDataItem_v2>();
            this.PrimitiveTypes = primitiveTypes;
            this.NamedTypes = namedTypes;
            this.Sizer = new TypeSizer(platform, this.NamedTypes);
        }

        public List<SerializedType> Types { get; private set; }
        public Dictionary<string, StructType_v1> StructsSeen { get; private set; }
        public Dictionary<string, UnionType_v1> UnionsSeen { get; private set; }
        public Dictionary<string, SerializedEnumType> EnumsSeen { get; private set; }
        public Dictionary<string, int> Constants { get; private set; }
        public Dictionary<string, SerializedType> NamedTypes { get; private set; }
        public Dictionary<string, PrimitiveType_v1> PrimitiveTypes { get; private set; }
        public List<ProcedureBase_v1> Procedures { get; private set; }
        public List<GlobalDataItem_v2> Variables { get; private set; }

        public TypeSizer Sizer { get; private set; }

        /// <summary>
        /// Given a C declaration, adds it to the symbol table 
        /// as a function or a type declaration.
        /// </summary>
        /// <param name="decl"></param>
        public List<SerializedType> AddDeclaration(Decl decl)
        {
            var types = new List<SerializedType>();
            if (decl is FunctionDecl fndec)
            {
                return types;
            }

            IEnumerable<DeclSpec> declspecs = decl.decl_specs;
            var isTypedef = false;
            if (decl.decl_specs[0] is StorageClassSpec scspec &&
                scspec.Type == CTokenType.Typedef)
            {
                declspecs = decl.decl_specs.Skip(1);
                isTypedef = true;
            }

            var ntde = new NamedDataTypeExtractor(platform, declspecs, this, pointerSize);
            foreach (var declarator in decl.init_declarator_list)
            {
                var nt = ntde.GetNameAndType(declarator.Declarator);
                var serType = nt.DataType!;

                if (nt.DataType is SerializedSignature sSig)
                {
                    sSig.Convention ??= GetCallingConventionFromAttributes(decl.attribute_list);
                    if (sSig.ReturnValue != null)
                    {
                        var (kind, _) = ntde.GetArgumentKindFromAttributes(
                            "returns", decl.attribute_list);
                        sSig.ReturnValue.Kind = kind;
                    }
                    var sProc = MakeProcedure(nt.Name!, sSig, decl.attribute_list);
                    Procedures.Add(sProc);
                    types.Add(sSig);
                }
                else if (!isTypedef)
                {
                    GlobalDataItem_v2 variable = MakeGlobalVariable(nt, serType, decl.attribute_list);
                    Variables.Add(variable);
                    types.Add(serType);
                }
                if (isTypedef)
                {
                    //$REVIEW: should make sure that if the typedef already exists, 
                    // then the types match but a real compiler would have validated that.
                    var typedef = new SerializedTypedef
                    {
                        Name = nt.Name,
                        DataType = serType
                    };
                    Types.Add(typedef);
                    //$REVIEW: do we really need to check for consistence?
                    NamedTypes[typedef.Name!] = serType;
                    types.Add(serType);
                }
            }
            return types;
        }

        private string? GetCallingConventionFromAttributes(List<CAttribute>? attributes)
        {
            var attrConvention = attributes?.Find(a =>
                a.Name.Components.Length == 2 &&
                a.Name.Components[0] == "reko" &&
                a.Name.Components[1] == "convention");
            if (attrConvention?.Tokens is null)
                return null;
            if (attrConvention.Tokens.Count == 1 && 
                attrConvention.Tokens[0].Type == CTokenType.Id)
            {
                return (string?) attrConvention.Tokens[0].Value;
            }
            throw new CParserException("Incorrect syntax for [[reko::convention]].");
        }

        private ProcedureBase_v1 MakeProcedure(string name, SerializedSignature sSig, List<CAttribute>? attributes)
        {
            var attrChar = attributes?.Find(a =>
                a.Name.Components.Length == 2 &&
                a.Name.Components[0] == "reko" &&
                a.Name.Components[1] == "characteristics");
            var c = MakeCharacteristics(attrChar);
            var attrService = attributes?.Find(a =>
                a.Name.Components.Length == 2 && 
                a.Name.Components[0] == "reko" &&
                a.Name.Components[1] == "service");
            var noreturn = attributes?.Find(a =>
                a.Name.Components.Length == 1 &&
                a.Name.Components[0] == "noreturn")
                is not null;
            if (noreturn)
            {
                if (c is null)
                    c = new ProcedureCharacteristics();
                c.Terminates = true;
            }
            if (attrService != null)
            {
                var sService = MakeService(name, sSig, attrService);
                sService.Characteristics = c;
                return sService;
            }
            else
            {
                var addr = FindRekoAddressAttribute(attributes);
                return new Procedure_v1
                {
                    Name = name,
                    Signature = sSig,
                    Address = addr,
                    Characteristics = c,
                };
            }
        }

        private static GlobalDataItem_v2 MakeGlobalVariable(NamedDataType nt, SerializedType serType, List<CAttribute>? attributes)
        {
            var sAddr = FindRekoAddressAttribute(attributes);
            return new GlobalDataItem_v2
            {
                Name = nt.Name,
                DataType = serType,
                Address = sAddr
            };
        }

        private static string? FindRekoAddressAttribute(List<CAttribute>? attributes)
        {
            string? addr = null;
            var attrAddress = attributes?.Find(a =>
                a.Name.Components.Length == 2 &&
                a.Name.Components[0] == "reko" &&
                a.Name.Components[1] == "address");
            if (attrAddress != null && attrAddress.Tokens is { })
            {
                if (attrAddress.Tokens.Count != 1 ||
                    attrAddress.Tokens[0].Type != CTokenType.StringLiteral)
                    throw new FormatException("[[reko::address]] attribute is malformed. Expected a string constant.");
                addr = (string?) attrAddress.Tokens[0].Value;
            }
            return addr;
        }

        private SerializedService MakeService(string name, SerializedSignature sSig, CAttribute attrService)
        {
            var sap = new ServiceAttributeParser(attrService);
            var syscall = sap.Parse();
            return new SerializedService
            {
                Signature = sSig,
                Name = name,
                SyscallInfo = syscall,
            };
        }

        private ProcedureCharacteristics? MakeCharacteristics(CAttribute? attrCharacteristics)
        {
            if (attrCharacteristics is null)
                return null;
            var cp = new CharacteristicsParser(attrCharacteristics);
            return cp.Parse();
        }
    }
}
