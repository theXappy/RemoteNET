﻿using System.Text;
using ScubaDiver.Demangle.Demangle.Core.Serialization;
using ScubaDiver.Demangle.Demangle.Core.Types;

namespace ScubaDiver.Demangle.Demangle
{
    public static class TypesRestarizer
    {
        private class TestSerializedTypeRenderer : ISerializedTypeVisitor<StringBuilder>
        {
            private StringBuilder sb;
            private string? name;
            private string? modifier;

            public TestSerializedTypeRenderer(StringBuilder sb)
            {
                this.sb = sb;
            }

            internal string Render(string modifier, string scope, string name, SerializedType sp)
            {
                this.modifier = modifier;
                this.name = name;
                if (scope != null)
                    this.name = scope + "::" + name;
                if (sp != null)
                    sp.Accept(this);
                else
                    sb.Append(this.name);
                return sb.ToString();
            }

            public StringBuilder VisitPrimitive(PrimitiveType_v1 primitive)
            {
                switch (primitive.Domain)
                {
                    case Domain.None:
                        sb.Append("void");
                        break;
                    case Domain.Boolean:
                        sb.Append("bool");
                        break;
                    case Domain.SignedInt:
                        switch (primitive.ByteSize)
                        {
                            case 1: sb.Append("int8_t"); break;
                            case 2: sb.Append("int16_t"); break;
                            case 4: sb.Append("int32_t"); break;
                            case 8: sb.Append("int64_t"); break;
                            default: throw new NotImplementedException();
                        }
                        break;
                    case Domain.UnsignedInt:
                        switch (primitive.ByteSize)
                        {
                            case 2: sb.Append("uint16_t"); break;
                            case 4: sb.Append("uint32_t"); break;
                            case 8: sb.Append("uint64_t"); break;
                            default: throw new NotImplementedException();
                        }
                        break;
                    case Domain.Character:
                        switch (primitive.ByteSize)
                        {
                            case 1: sb.Append("char"); break;
                            case 2: sb.Append("wchar_t"); break;
                        }
                        break;
                    case Domain.Character | Domain.UnsignedInt:
                        switch (primitive.ByteSize)
                        {
                            case 1: sb.Append("char"); break;
                            default: throw new NotImplementedException();
                        }
                        break;
                    case Domain.Real:
                        switch (primitive.ByteSize)
                        {
                            case 4: sb.Append("float"); break;
                            case 8: sb.Append("double"); break;
                            default: throw new NotImplementedException();
                        }
                        break;
                    default:
                        throw new NotSupportedException(string.Format("Domain {0} is not supported.", primitive.Domain));
                }
                if (name != null)
                    sb.AppendFormat(" {0}", name);
                return sb;
            }

            public StringBuilder VisitPointer(PointerType_v1 pointer)
            {
                var n = name;
                name = null;
                if (pointer == null || pointer.DataType == null)
                    throw new ArgumentNullException("VisitPointer received a null argument");
                pointer.DataType.Accept(this);
                // SS: Removed a space before the *
                sb.AppendFormat("*");
                name = n;
                if (name != null)
                    sb.AppendFormat(" {0}", name);
                return sb;
            }

            public StringBuilder VisitReference(ReferenceType_v1 reference)
            {
                var n = name;
                name = null;
                if (reference == null || reference.Referent == null)
                    throw new ArgumentNullException("VisitReference received null as argument");
                reference.Referent.Accept(this);
                // SS: Removed a space before the &
                sb.AppendFormat("&");
                name = n;
                if (name != null)
                    sb.AppendFormat(" {0}", name);
                return sb;
            }

            public StringBuilder VisitMemberPointer(MemberPointer_v1 memptr)
            {
                var n = name;
                if (memptr == null || memptr.DeclaringClass == null)
                    throw new ArgumentNullException("VisitMemberPointer received a null argument");
                memptr.DeclaringClass.Accept(this);
                sb.Append("::*");
                sb.Append(n);
                return sb;
            }

            public StringBuilder VisitArray(ArrayType_v1 array)
            {
                throw new NotImplementedException();
            }

            public StringBuilder VisitCode(CodeType_v1 array)
            {
                throw new NotImplementedException();
            }

            public StringBuilder VisitSignature(SerializedSignature signature)
            {
                if (!string.IsNullOrEmpty(modifier))
                    sb.AppendFormat("{0}: ", modifier);
                if (signature.ReturnValue != null && signature.ReturnValue.Type != null)
                {
                    // void __cdecl
                    signature.ReturnValue.Type.Accept(this);
                    sb.Append(" ");
                    if (!string.IsNullOrEmpty(signature.Convention))
                        sb.AppendFormat("{0}", signature.Convention);
                }
                else
                {
                    // __cdecl myFunc
                    if (!string.IsNullOrEmpty(signature.Convention))
                        sb.AppendFormat("{0}", signature.Convention);
                    sb.Append(" ");
                    sb.Append(name);
                }
                sb.Append("(");
                string sep = "";
                if (signature.Arguments == null)
                    throw new ArgumentNullException("VisitSignature received signature.Arguments = null");
                foreach (var arg in signature.Arguments)
                {
                    sb.Append(sep);
                    sep = ", ";
                    name = arg.Name;
                    if (arg.Type == null)
                        throw new ArgumentNullException("VisitSignature received signature.Arguments where one of the arguments arg.Type = null");
                    arg.Type.Accept(this);
                }
                sb.Append(")");
                return sb;
            }
            public List<RestarizedParameter> HackArgs(SerializedSignature signature)
            {
                if (signature == null || signature.Arguments == null)
                    throw new ArgumentNullException("HackArgs received a null signature argument");

                List<RestarizedParameter> output = new List<RestarizedParameter>();
                foreach (Argument_v1 arg in signature.Arguments)
                {
                    output.Add(HackArg(arg));
                }
                return output;
            }
            public RestarizedParameter HackArg(Argument_v1 arg)
            {
                sb = new StringBuilder();
                this.name = arg.Name;
                if (arg == null || arg.Type == null)
                    throw new ArgumentNullException("HackArg received a null argument");
                arg.Type.Accept(this);
                return new RestarizedParameter(sb.ToString(), arg);
            }


            public StringBuilder VisitString(StringType_v2 str)
            {
                throw new NotImplementedException();
            }

            public StringBuilder VisitStructure(StructType_v1 structure)
            {
                sb.Append(structure.Name);
                return sb;
            }

            public StringBuilder VisitTypedef(SerializedTypedef typedef)
            {
                throw new NotImplementedException();
            }

            public StringBuilder VisitTypeReference(TypeReference_v1 typeReference)
            {
                if (typeReference.Scope != null)
                {
                    for (var i = 0; i < typeReference.Scope.Length; i++)
                    {
                        sb.Append($"{typeReference.Scope[i]}::");
                    }
                }
                sb.Append(typeReference.TypeName);
                if (name != null)
                    sb.AppendFormat(" {0}", name);
                if (typeReference.TypeArguments != null && typeReference.TypeArguments.Length > 0)
                {
                    sb.Append("<");
                    var sep = "";
                    foreach (var tyArg in typeReference.TypeArguments)
                    {
                        sb.Append(sep);
                        tyArg.Accept(this);
                        sep = ",";
                    }
                    sb.Append(">");
                }
                return sb;
            }

            public StringBuilder VisitUnion(UnionType_v1 union)
            {
                throw new NotImplementedException();
            }

            public StringBuilder VisitEnum(SerializedEnumType serializedEnumType)
            {
                sb.AppendFormat("enum {0}", serializedEnumType.Name);
                return sb;
            }

            public StringBuilder VisitTemplate(SerializedTemplate template)
            {
                var n = name;
                sb.Append(template.Name);
                sb.Append("<");
                var sep = "";
                foreach (var typeArg in template.TypeArguments)
                {
                    sb.Append(sep);
                    typeArg.Accept(this);
                }
                sb.Append(">");

                name = n;
                if (name != null)
                    sb.AppendFormat(" {0}", name);
                return sb;
            }
            public StringBuilder VisitVoidType(VoidType_v1 serializedVoidType)
            {
                sb.Append("void");
                if (name != null)
                    sb.AppendFormat(" {0}", name);
                return sb;
            }
        }

        public static List<RestarizedParameter> RestarizeParameters(string mangledFuncSig)
        {
            var p = new MsMangledNameParser(mangledFuncSig);
            var sp = p.Parse();
            if (sp.Item2 is not SerializedSignature sig)
            {
                throw new Exception("MsMangledNameParser.Parse's return value did not contain a SerializedSignature");
            }
            return RestarizeParameters(sig);
        }
        public static List<RestarizedParameter> RestarizeParameters(SerializedSignature parsedSig)
        {
            var sb = new StringBuilder();
            var renderer = new TestSerializedTypeRenderer(sb);
            return renderer.HackArgs(parsedSig);
        }
        public static RestarizedParameter RestarizeArgument(Argument_v1 arg)
        {
            var sb = new StringBuilder();
            var renderer = new TestSerializedTypeRenderer(sb);
            return renderer.HackArg(arg);
        }
    }

    public class RestarizedParameter
    {
        public string FriendlyName { get; set; }
        public Argument_v1 Argument { get; set; }

        public RestarizedParameter(string friendlyName, Argument_v1 argument)
        {
            FriendlyName = friendlyName;
            Argument = argument;
        }

        public override string ToString() => FriendlyName;
    }
}
