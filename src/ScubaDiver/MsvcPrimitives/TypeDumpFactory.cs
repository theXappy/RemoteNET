using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.Rtti;
using System.Collections.Generic;
using System.Linq;

namespace ScubaDiver
{
    public static class TypeDumpFactory
    {
        public static TypeDump ConvertMsvcTypeToTypeDump(MsvcType matchingType)
        {
            TypeInfo typeInfo = matchingType.TypeInfo;
            ModuleInfo module = matchingType.Module.ModuleInfo;

            List<TypeDump.TypeField> fields = new();
            List<TypeDump.TypeMethod> methods = new();
            List<TypeDump.TypeMethod> constructors = new();
            List<TypeDump.TypeMethodTable> vftables = new();
            DeconstructRttiType(matchingType, fields, constructors, methods, vftables);

            TypeDump recusiveTypeDump = new TypeDump()
            {
                Assembly = module.Name,
                FullTypeName = typeInfo.NamespaceAndName,
                Methods = methods,
                Constructors = constructors,
                Fields = fields,
                MethodTables = vftables
            };
            return recusiveTypeDump;
        }
        private static void DeconstructRttiType(MsvcType type,
          List<TypeDump.TypeField> fields,
          List<TypeDump.TypeMethod> constructors,
          List<TypeDump.TypeMethod> methods,
          List<TypeDump.TypeMethodTable> vftables)
        {
            var typeInfo = type.TypeInfo;

            string className = typeInfo.Name;
            string classMembersPrefix = $"{typeInfo.NamespaceAndName}::";
            string ctorName = $"{classMembersPrefix}{className}"; // Constructing NameSpace::ClassName::ClassName
            string vftableName = $"{classMembersPrefix}`vftable'"; // Constructing NameSpace::ClassName::`vftable'

            foreach (VftableInfo vftable in type.GetVftables())
            {
                HandleVftable(vftable, vftableName, fields);

                // Keep vftable aside so we can also gather functions from it
                // Handle both exported and non-exported vftables
                if (vftable.ExportedField != null)
                {
                    // Exported vftable
                    vftables.Add(new TypeDump.TypeMethodTable
                    {
                        DecoratedName = vftable.ExportedField.DecoratedName,
                        UndecoratedFullName = vftable.ExportedField.UndecoratedName,
                        XoredAddress = (long)vftable.ExportedField.XoredAddress,
                    });
                }
                else
                {
                    // Non-exported vftable (RTTI-based)
                    vftables.Add(new TypeDump.TypeMethodTable
                    {
                        DecoratedName = vftable.Name,
                        UndecoratedFullName = vftable.Name,
                        XoredAddress = (long)(vftable.Address ^ FirstClassTypeInfo.XorMask),
                    });
                }
                continue;
            }

            foreach (ISymbolBackedMember member in type.GetMembers().OfType<ISymbolBackedMember>())
            {
                UndecoratedSymbol dllExport = member.Symbol;
                if (dllExport is UndecoratedFunction undecoratedFunc)
                {
                    TypeDump.TypeMethod typeMethod = VftableParser.ConvertToTypeMethod(undecoratedFunc);
                    if (typeMethod == null)
                        continue;

                    // Check for inheritance.
                    // Heuristic: if the name this function was exported with does not start with this classes prefix, it's inherited
                    if (!typeMethod.UndecoratedFullName.StartsWith(classMembersPrefix))
                    {
                        typeMethod.IsInherited = true;
                    }

                    if (typeMethod.UndecoratedFullName == ctorName)
                        constructors.Add(typeMethod);
                    else
                        methods.Add(typeMethod);
                }
                else if (dllExport is UndecoratedExportedField undecField)
                {
                    bool isVftable = undecField.UndecoratedFullName.Contains("`vftable'");
                    if (isVftable)
                    {
                        // Already handled those lol
                        continue;
                    }

                    HandleTypeField(undecField, fields);
                }
            }
        }

        private static bool HandleVftable(VftableInfo vftableInfo, string vftableName,
            List<TypeDump.TypeField> fields)
        {
            // vftable gets a special treatment because we need it outside this func.
            // Handle both exported and non-exported vftables
            
            if (vftableInfo.ExportedField != null)
            {
                // Exported vftable - check the name
                UndecoratedExportedField undecField = vftableInfo.ExportedField;
                
                // TODO: BUG?? What if it's a "special" vftable for specific parent??
                if (undecField.UndecoratedFullName != vftableName)
                    return false;

                fields.Add(new TypeDump.TypeField()
                {
                    Name = "vftable",
                    TypeFullName = undecField.UndecoratedFullName,
                    Visibility = "Public"
                });
            }
            else
            {
                // Non-exported vftable (RTTI-based) - just add it without name check
                fields.Add(new TypeDump.TypeField()
                {
                    Name = vftableInfo.Name,
                    TypeFullName = vftableInfo.Name,
                    Visibility = "Public"
                });
            }

            return true;
        }

        private static void HandleTypeField(UndecoratedExportedField undecField,
            List<TypeDump.TypeField> fields)
        {
            fields.Add(new TypeDump.TypeField()
            {
                Name = undecField.UndecoratedName,
                TypeFullName = undecField.UndecoratedFullName,
                Visibility = "Public"
            });
        }
    }
}
