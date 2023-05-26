using Reko.Core.Serialization;
using Reko.Core.Types;
using Reko.Environments.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteNET.RttiReflection.Demangle
{
    public static class TypesRestarizer
    {
        private class TestSerializedTypeRenderer : ISerializedTypeVisitor<StringBuilder>
        {
            private StringBuilder sb;
            private string name;
            private string modifier;

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
                            case 8: sb.Append("__int64"); break;
                            default: throw new NotImplementedException();
                        }
                        break;
                    case Domain.UnsignedInt:
                        switch (primitive.ByteSize)
                        {
                            case 2: sb.Append("uint16_t"); break;
                            case 4: sb.Append("uint32_t"); break;
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
                pointer.DataType.Accept(this);
                sb.AppendFormat(" *");
                name = n;
                if (name != null)
                    sb.AppendFormat(" {0}", name);
                return sb;
            }

            public StringBuilder VisitReference(ReferenceType_v1 reference)
            {
                var n = name;
                name = null;
                reference.Referent.Accept(this);
                sb.AppendFormat(" ^");
                name = n;
                if (name != null)
                    sb.AppendFormat(" {0}", name);
                return sb;
            }

            public StringBuilder VisitMemberPointer(MemberPointer_v1 memptr)
            {
                var n = name;
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
                if (!string.IsNullOrEmpty(signature.Convention))
                    sb.AppendFormat("{0} ", signature.Convention);
                if (!string.IsNullOrEmpty(modifier))
                    sb.AppendFormat("{0}: ", modifier);
                if (signature.ReturnValue != null && signature.ReturnValue.Type != null)
                {
                    signature.ReturnValue.Type.Accept(this);
                }
                else
                {
                    sb.Append(name);
                }
                sb.Append("(");
                string sep = "";
                foreach (var arg in signature.Arguments)
                {
                    sb.Append(sep);
                    sep = ", ";
                    this.name = arg.Name;
                    arg.Type.Accept(this);
                }
                sb.Append(")");
                return sb;
            }

            public List<RestarizedParameter> HackArgs(SerializedSignature signature)
            {
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
                arg.Type.Accept(this);
                return new RestarizedParameter(sb.ToString(), arg);
            }


            public StringBuilder VisitString(StringType_v2 str)
            {
                throw new NotImplementedException();
            }

            public StringBuilder VisitStructure(StructType_v1 structure)
            {
                throw new NotImplementedException();
            }

            public StringBuilder VisitTypedef(SerializedTypedef typedef)
            {
                throw new NotImplementedException();
            }

            public StringBuilder VisitTypeReference(TypeReference_v1 typeReference)
            {
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
            return RestarizeParameters(sp.Item2 as SerializedSignature);
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
