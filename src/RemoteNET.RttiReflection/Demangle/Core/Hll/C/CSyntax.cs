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


using System;
using System.Collections.Generic;
using System.Text;

namespace Reko.Core.Hll.C
{
    public abstract class CSyntax
    {
        public abstract T Accept<T>(CSyntaxVisitor<T> visitor);
    }

    public class Decl : CSyntax
    {
        public List<CAttribute>? attribute_list;
        public List<DeclSpec> decl_specs;
        public List<InitDeclarator> init_declarator_list;

        public Decl(List<CAttribute>? attrs, List<DeclSpec> declSpecs, List<InitDeclarator> listDecls)
        {
            this.attribute_list = attrs;
            this.decl_specs = declSpecs;
            this.init_declarator_list = listDecls;
        }

        public override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitDeclaration(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("(decl");
            if (attribute_list != null)
            {
                sb.Append(' ');
                sb.Append(string.Join(' ', attribute_list));
            }
            foreach (var ds in decl_specs)
            {
                sb.Append(' ');
                sb.Append(ds);
            }
            if (init_declarator_list != null && init_declarator_list.Count > 0)
            {
                var sep = " (";
                foreach (var init in init_declarator_list)
                {
                    sb.AppendFormat("{0}{1}", sep, init);
                    sep = " ";
                }
                sb.Append(')');
            }
            sb.Append(')');
            return sb.ToString();
        }
    }

    public class FunctionDecl : Decl
    {
        public Decl Signature;
        public List<Stat> Body;
        public CTokenType calling_convention;

        public FunctionDecl(Decl signature, List<Stat> body, List<DeclSpec> declSpecs) 
            : base(null, declSpecs, new())
        {
            Signature = signature;
            Body = body;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("(fndecl ");
            sb.Append(Signature);
            sb.Append(" (");
            var sep = false;
            foreach (var s in Body)
            {
                if (sep)
                    sb.AppendLine();
                sb.Append(s);
                sep = true;
            }
            sb.Append("))");
            return sb.ToString();
        }
    }

    public interface DeclSpecVisitor<T>
    {
        T VisitSimpleType(SimpleTypeSpec simpleType);
        T VisitTypedef(TypeDefName typeDefName);
        T VisitComplexType(ComplexTypeSpec complexType);
        T VisitStorageClass(StorageClassSpec storageClass);
        T VisitTypeQualifier(TypeQualifier typeQualifier);
        T VisitEnum(EnumeratorTypeSpec enumeratorTypeSpec);
        T VisitExtendedDeclspec(ExtendedDeclspec extendedDeclspec);
    }

    public abstract class DeclSpec : CSyntax
    {
        public sealed override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitDeclSpec(this);
        }

        public abstract T Accept<T>(DeclSpecVisitor<T> visitor);
    }

    public class ExtendedDeclspec : DeclSpec
    {
        public string Name;

        public ExtendedDeclspec(string name)
        {
            this.Name = name;
        }

        public override T Accept<T>(DeclSpecVisitor<T> visitor)
        {
            return visitor.VisitExtendedDeclspec(this);
        }

        public override string ToString()
        {
            return string.Format("(__declspec {0})", Name);
        }
    }

    public abstract class TypeSpec : DeclSpec
    {
    }

    public class TypeQualifier : DeclSpec
    {
        public CTokenType Qualifier;

        public override T Accept<T>(DeclSpecVisitor<T> visitor)
        {
            return visitor.VisitTypeQualifier(this);
        }

        public override string ToString() { return Qualifier.ToString(); }
    }

    public class StorageClassSpec : DeclSpec
    {
        public CTokenType Type;

        public override T Accept<T>(DeclSpecVisitor<T> visitor)
        {
            return visitor.VisitStorageClass(this);
        }

        public override string ToString() { return Type.ToString(); }
    }

    public class SimpleTypeSpec : TypeSpec
    {
        public CTokenType Type;

        public override T Accept<T>(DeclSpecVisitor<T> visitor)
        {
            return visitor.VisitSimpleType(this);
        }

        public override string ToString() { return Type.ToString(); }
    }

    public class TypeDefName : TypeSpec
    {
        public string Name;

        public TypeDefName(string name)
        {
            this.Name = name;
        }

        public override T Accept<T>(DeclSpecVisitor<T> visitor)
        {
            return visitor.VisitTypedef(this);
        }

        public override string ToString() { return Name; }
    }

    public class CType : CSyntax
    {
        public List<DeclSpec> DeclSpecList;
        public Declarator? Declarator;

        public CType(List<DeclSpec> declSpecList, Declarator? decl)
        {
            DeclSpecList = declSpecList;
            Declarator = decl;
        }

        public override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitType(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            var sep = '(';
            foreach (var x in DeclSpecList)
            {
                sb.Append(sep);
                sb.Append(x);
                sep = ' ';
            }
            sb.Append(sep);
            sb.Append(Declarator);
            sb.Append(')');
            return sb.ToString();
        }
    }

    public class ComplexTypeSpec : TypeSpec
    {
        public CTokenType Type;
        public string Name;
        public List<StructDecl> DeclList;
        public int Alignment;

        public ComplexTypeSpec(CTokenType type, string name, List<StructDecl> declList, int alignment)
        {
            Type = type;
            Name = name;
            DeclList = declList;
            Alignment = alignment;
        }

        public override T Accept<T>(DeclSpecVisitor<T> visitor)
        {
            return visitor.VisitComplexType(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("({0} ", Type);
            if (Alignment != 0)
                sb.AppendFormat("{0} ", Alignment);
            if (!string.IsNullOrEmpty(Name))
                sb.AppendFormat("{0}", Name);
            if (!IsForwardDeclaration())
            {
                var sep = " (";
                foreach (var sd in DeclList)
                {
                    sb.AppendFormat("{0}{1}", sep, sd);
                    sep = " ";
                }
            }
            sb.Append(')');
            return sb.ToString();
        }

        public bool IsForwardDeclaration()
        {
            return DeclList == null;
        }
    }

    public class EnumeratorTypeSpec : TypeSpec
    {
        public string? Tag;
        public List<Enumerator> Enums;

        public EnumeratorTypeSpec(string? tag, List<Enumerator> enums)
        {
            Tag = tag;
            Enums = enums;
        }

        public override T Accept<T>(DeclSpecVisitor<T> visitor)
        {
            return visitor.VisitEnum(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("(Enum");
            if (!string.IsNullOrEmpty(Tag))
            {
                sb.AppendFormat(" {0}", Tag);
            }
            var sep = " (";
            foreach (var e in Enums)
            {
                sb.Append(sep);
                sep = " ";
                sb.Append(e);
            }
            sb.Append("))");
                return sb.ToString();
        }
    }

    public class Enumerator : CSyntax
    {
        public string Name;
        public CExpression? Value;

        public Enumerator(string name, CExpression? value)
        {
            this.Name = name;
            this.Value = value;
        }

        public override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitEnumerator(this);
        }

        public override string ToString()
        {
            return (Value == null)
                ? string.Format("({0})", Name)
                : string.Format("({0} {1})", Name, Value);
        }
    }

    public interface DeclaratorVisitor<T>
    {
        T VisitId(IdDeclarator idDeclarator);
        T VisitArray(ArrayDeclarator array);
        T VisitField(FieldDeclarator field);
        T VisitPointer(PointerDeclarator pointer);
        T VisitReference(ReferenceDeclarator pointer);
        T VisitFunction(FunctionDeclarator function);
        T VisitCallConvention(CallConventionDeclarator callConvention);
    }

    public class InitDeclarator : CSyntax
    {
        public Declarator Declarator;
        public Initializer? Init;

        public InitDeclarator(Declarator decl, Initializer? init)
        {
            this.Declarator = decl;
            this.Init = init;
        }

        public override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitInitDeclarator(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("(init-decl ");
            sb.Append(Declarator);
            if (this.Init != null)
            {
                sb.AppendFormat(" {0}", Init);
            }
            sb.Append(')');
            return sb.ToString();
        }
    }

    public abstract class Declarator
    {
        public abstract T Accept<T>(DeclaratorVisitor<T> visitor);
    }

    public class ParamDecl : CSyntax
    {
        public List<CAttribute>? Attributes;
        public List<DeclSpec>? DeclSpecs;
        public Declarator? Declarator;
        public bool IsEllipsis;

        public ParamDecl(List<CAttribute>? attributes, List<DeclSpec>? declSpecs, Declarator? declarator, bool isEllipsis)
        {
            Attributes = attributes;
            DeclSpecs = declSpecs;
            Declarator = declarator;
            IsEllipsis = isEllipsis;
        }

        public override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitParamDeclaration(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            if (Attributes != null)
            {
                sb.Append(string.Join(' ', Attributes));
                sb.Append(' ');
            }
            if (DeclSpecs != null)
            {
                foreach (var declspec in DeclSpecs)
                {
                    sb.Append(declspec);
                    sb.Append(' ');
                }
            }
            sb.Append(Declarator);
            sb.Append(')');
            return sb.ToString();
        }
    }

    public class PointerDeclarator : Declarator
    {
        public Declarator? Pointee;
        public List<TypeQualifier>? TypeQualifierList;

        public override T Accept<T>(DeclaratorVisitor<T> visitor)
        {
            return visitor.VisitPointer(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("(ptr");
            if (TypeQualifierList != null && TypeQualifierList.Count > 0)
            {
                foreach (var tq in TypeQualifierList)
                {
                    sb.AppendFormat(" {0}", tq);
                }
            }
            sb.AppendFormat(" {0})", Pointee);
            return sb.ToString();
        }
    }

    public class ReferenceDeclarator : Declarator
    {
        public Declarator? Referent;
        public List<TypeQualifier>? TypeQualifierList;

        public override T Accept<T>(DeclaratorVisitor<T> visitor)
        {
            return visitor.VisitReference(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("(ref");
            if (TypeQualifierList != null && TypeQualifierList.Count > 0)
            {
                foreach (var tq in TypeQualifierList)
                {
                    sb.AppendFormat(" {0}", tq);
                }
            }
            sb.AppendFormat(" {0})", Referent);
            return sb.ToString();
        }

    }

    public class IdDeclarator : Declarator
    {
        public string Name;

        public IdDeclarator(string name)
        {
            this.Name = name;
        }

        public override T Accept<T>(DeclaratorVisitor<T> visitor)
        {
            return visitor.VisitId(this);
        }

        public override string ToString() { return Name; }
    }

    public class FunctionDeclarator : Declarator
    {
        public Declarator Declarator;
        public List<ParamDecl> Parameters;

        public FunctionDeclarator(Declarator declarator, List<ParamDecl> parameters)
        {
            Declarator = declarator;
            Parameters = parameters;
        }

        public override T Accept<T>(DeclaratorVisitor<T> visitor)
        {
            return visitor.VisitFunction(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("(func ");
            sb.Append(Declarator);
            if (this.Parameters is null)
            {
                sb.Append(')');
                return sb.ToString();
            }
            var sep = " (";
            foreach (var param in Parameters)
            {
                sb.Append(sep);
                sb.Append(param);
                sep = " ";
            }
            sb.Append("))");
            return sb.ToString();
        }
    }

    public class CallConventionDeclarator : Declarator
    {
        public CTokenType Convention;
        public Declarator Declarator;

        public CallConventionDeclarator(CTokenType convention, Declarator declarator)
        {
            Convention = convention;
            Declarator = declarator;
        }

        public override T Accept<T>(DeclaratorVisitor<T> visitor)
        {
            return visitor.VisitCallConvention(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("({0} {1})", Convention, Declarator);
            return sb.ToString();
        }
    }

    public class ArrayDeclarator : Declarator
    {
        public Declarator Declarator;
        public CExpression? Size;

        public ArrayDeclarator(Declarator declarator, CExpression? size)
        {
            Declarator = declarator;
            Size = size;
        }

        public override T Accept<T>(DeclaratorVisitor<T> visitor)
        {
            return visitor.VisitArray(this);
        }

        public override string ToString() { return string.Format("(arr {0} {1})", Declarator, Size); }
    }

    public class StructDecl : CSyntax
    {
        public List<DeclSpec> SpecQualifierList;
        public List<FieldDeclarator> FieldDeclarators;
        public List<CAttribute>? AttributeList;

        public StructDecl(List<DeclSpec> sql, List<FieldDeclarator> decls, List<CAttribute>? attrs)
        {
            SpecQualifierList = sql;
            FieldDeclarators = decls;
            AttributeList = attrs;
        }

        public override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            var sep = "";
            foreach (var sq in SpecQualifierList)
            {
                sb.AppendFormat("{0}{1}", sep, sq);
                sep = " ";
            }
            sb.Append(") (");
            sep = "";
            foreach (var d in FieldDeclarators)
            {
                sb.AppendFormat("{0}{1}", sep, d);
                sep = " ";
            }
            sb.Append(')');
            if (AttributeList != null && AttributeList.Count > 0)
            {
                sb.Append(" (");
                sb.Append(string.Join(' ', AttributeList));
                sb.Append(')');
            }
            return sb.ToString();
        }
    }

    public class FieldDeclarator : Declarator
    {
        public Declarator Declarator;
        public CExpression? FieldSize;

        public FieldDeclarator(Declarator declarator, CExpression? fieldSize)
        {
            Declarator = declarator;
            FieldSize = fieldSize;
        }

        public override T Accept<T>(DeclaratorVisitor<T> visitor)
        {
            return visitor.VisitField(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("({0}", Declarator);
            if (FieldSize != null)
                sb.AppendFormat(" {0}", FieldSize);
            sb.Append(')');
            return sb.ToString();
        }
    }

    public abstract class Initializer
    {
    }

    public class ListInitializer : Initializer
    {
        public ListInitializer()
        {
            List = new List<Initializer>();
        }

        public List<Initializer> List { get; }
    }

    public class ExpressionInitializer : Initializer
    {
        public CExpression Expression;

        public ExpressionInitializer(CExpression expression)
        {
            this.Expression = expression;
        }

        public override string ToString() { return Expression.ToString()!; }
    }

    public class CAttribute : CSyntax
    {
        public QualifiedName Name;
        public List<CToken>? Tokens;

        public override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitAttribute(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("(attr {0}", Name);
            if (Tokens != null && Tokens.Count > 0)
            {
                sb.Append(" (");
                foreach (var token in Tokens)
                {
                    sb.AppendFormat("{0} {1}", token.Type, token.Value);
                }
                sb.Append(')');
            }
            sb.Append(')');
            return sb.ToString();
        }
    }

    #region Expressions

    public abstract class CExpression : CSyntax
    {
        public sealed override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }

        public abstract T Accept<T>(CExpressionVisitor<T> visitor);
    }

    public class ConstExp : CExpression
    {
        public object Const;

        public ConstExp(object value)
        {
            this.Const = value;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitConstant(this);
        }

        public override string ToString() { return Const != null ? Const.ToString()! : ""; }

    }

    public class CIdentifier : CExpression
    {
        public string Name;

        public CIdentifier(string name)
        {
            this.Name = name;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitIdentifier(this);
        }

        public override string ToString() { return Name; }

    }

    public class Application : CExpression
    {
        public CExpression Function;
        public List<CExpression> Arguments;

        public Application(CExpression function, List<CExpression> arguments)
        {
            Function = function;
            Arguments = arguments;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitApplication(this);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            sb.Append(Function);
            foreach (var exp in Arguments)
            {
                sb.Append(' ');
                sb.Append(exp);
            }
            sb.Append(')');
            return sb.ToString();
        }
    }

    public class MemberExpression : CExpression
    {
        public CExpression Expression;
        public string FieldName;
        public bool Dereference;

        public MemberExpression(CExpression exp, bool deref, string fieldName)
        {
            this.Expression = exp;
            this.FieldName = fieldName;
            this.Dereference = deref;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitMember(this);
        }

        public override string ToString() { return string.Format("({0} {1} {2}",
            Expression,
            Dereference ? "->" : ".",
            FieldName); }
    }
    public class CUnaryExpression : CExpression
    {
        public CTokenType Operation;
        public CExpression Expression;

        public CUnaryExpression(CTokenType operation, CExpression expression)
        {
            Operation = operation;
            Expression = expression;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitUnary(this);
        }

        public override string ToString() { return string.Format("({0} {1})", Operation, Expression); }
    }

    public class CBinaryExpression : CExpression
    {
        public CTokenType Operation;
        public CExpression Left;
        public CExpression Right;

        public CBinaryExpression(CTokenType operation, CExpression left, CExpression right)
        {
            Operation = operation;
            Left = left;
            Right = right;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitBinary(this);
        }


        public override string ToString() { return string.Format("({0} {1} {2})", Operation, Left, Right); }
    }

    public class AssignExpression : CExpression
    {
        public CTokenType AssingmentOp;
        public CExpression LValue;
        public CExpression RValue;

        public AssignExpression(CTokenType assingmentOp, CExpression lValue, CExpression rValue)
        {
            AssingmentOp = assingmentOp;
            LValue = lValue;
            RValue = rValue;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitAssign(this);
        }
    }

    public class CastExpression : CExpression
    {
        public CType Type;
        public CExpression Expression;

        public CastExpression(CType type, CExpression expression)
        {
            Type = type;
            Expression = expression;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitCast(this);
        }

        public override string ToString()
        {
            return string.Format("(cast {0} {1})", Type, Expression);
        }
    }

    public class ConditionalExpression : CExpression
    {
        public CExpression Condition;
        public CExpression Consequent;
        public CExpression Alternative;

        public ConditionalExpression(CExpression condition, CExpression consequent, CExpression alternative)
        {
            Condition = condition;
            Consequent = consequent;
            Alternative = alternative;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitConditional(this);
        }

        public override string ToString()
        {
            return string.Format("(cond {0} {1} {2})", Condition, Consequent, Alternative);
        }
    }

    public class IncrementExpression : CExpression
    {
        public CTokenType Incrementor;
        public bool Prefix;
        public CExpression Expression;

        public IncrementExpression(CTokenType incrementor, bool prefix, CExpression expression)
        {
            Incrementor = incrementor;
            Prefix = prefix;
            Expression = expression;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitIncrement(this);
        }
    }

    public class SizeofExpression : CExpression
    {
        public CType? Type;
        public CExpression? Expression;

        public SizeofExpression(CType? type)
        {
            Type = type;
        }
        
        public SizeofExpression(CExpression? expression)
        {
            Expression = expression;
        }

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitSizeof(this);
        }

        public override string ToString()
        {
            return string.Format("(sizeof {0})",
                Type != null ? (object) Type : (object) Expression!);
        }
    }

    public class CArrayAccess : CExpression
    {
        public CExpression Expression;
        public CExpression Index;

        public CArrayAccess(CExpression expression, CExpression index)
        {
            Expression = expression;
            Index = index;
        }   

        public override T Accept<T>(CExpressionVisitor<T> visitor)
        {
            return visitor.VisitArrayAccess(this);
        }

        public override string ToString()
        {
            return $"(aref {Expression} {Index})";
        }
    }

    #endregion

    #region Statements

    public abstract class Stat : CSyntax
    {
        public override T Accept<T>(CSyntaxVisitor<T> visitor)
        {
            return visitor.VisitStatement(this);
        }
    }

    public class LabeledStat : Stat
    {
        public Label Label;
        public Stat Stat;

        public LabeledStat(Label label, Stat stat)
        {
            Label = label;
            Stat = stat;
        }
    }

    public class DeclStat : Stat
    {
        public Decl Declaration;
        public CExpression? Initializer;

        public DeclStat(Decl declaration, CExpression? initializer)
        {
            Declaration = declaration;
            Initializer = initializer;
        }
    }

    public abstract class Label
    {
    }

    public class LineLabel : Label
    {
        public string Name;

        public LineLabel(string name)
        {
            Name = name;
        }

        public override string ToString() { return $"(label {Name})"; }
    }

    public class CaseLabel : Label
    {
        public CExpression Value;

        public CaseLabel(CExpression value)
        {
            Value = value;
        }

        public override string ToString()
        {
            if (Value == null)
                return "(default)";
            else
                return string.Format("(case {0})", Value);
        }
    }

    public class IfStat : Stat
    {
        public CExpression Expression;
        public Stat Consequence;
        public Stat? Alternative;

        public IfStat(CExpression expression, Stat consequence, Stat? alternative)
        {
            Expression = expression;
            Consequence = consequence;
            Alternative = alternative;
        }
    }

    public class WhileStat : Stat
    {
        public CExpression Expression;
        public Stat Body;

        public WhileStat(CExpression expression, Stat body)
        {
            Expression = expression;
            Body = body;
        }
    }

    public class DoWhileStat : Stat
    {
        public Stat Body;
        public CExpression Expression;

        public DoWhileStat(Stat body, CExpression expression)
        {
            Body = body;
            Expression = expression;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("(do ");
            sb.Append(Body);
            sb.Append(") ");
            sb.Append(Expression);
            sb.Append(')');
            return sb.ToString();
        }
    }

    public class ForStat : Stat
    {
        public Stat? Initializer;
        public CExpression? Test;
        public CExpression? Update;
        public Stat Body;

        public ForStat(Stat? initializer, CExpression? test, CExpression? update, Stat body)
        {
            Initializer = initializer;
            Test = test;
            Update = update;
            Body = body;
        }
    }

    public class GotoStat : Stat
    {
        public string Label;

        public GotoStat(string label)
        {
            this.Label = label;
        }
    }

    public class ReturnStat : Stat
    {
        public CExpression Expression;

        public ReturnStat(CExpression exp)
        {
            Expression = exp;
        }
    }

    public class ExprStat : Stat
    {
        public CExpression? Expression;

        public ExprStat(CExpression? exp)
        {
            this.Expression = exp;
        }

        public override string ToString()
        {
            if (Expression == null)
                return " ";
            return string.Format("({0})", Expression.ToString());
        }
    }

    public class CompoundStatement : Stat
    {
        public List<Stat> Statements;

        public CompoundStatement(List<Stat> statements)
        {
            Statements = statements;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('(');
            foreach (var stat in Statements)
            {
                sb.Append(stat);
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
    #endregion

    public struct QualifiedName
    {
        public readonly string[] Components;

        public QualifiedName(params string[] components)
        {
            this.Components = components;
        }

        public override string ToString()
        {
            return string.Join("::", Components);
        }
    }
}
