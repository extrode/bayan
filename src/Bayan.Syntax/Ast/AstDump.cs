using System.Text;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Parsing;

namespace Bayan.Syntax.Ast;

/// <summary>
/// Text form of a syntax tree, used by <c>bayan parse --ast</c> and the <c>parse/</c> golden tests.
/// Declarations and statements print one per line, indented; expressions, patterns and types print
/// on one line as S-expressions so precedence is explicit.
/// </summary>
public static class AstDump
{
    public static string Dump(SyntaxTree tree) => Dump(tree, Style.Plain);

    /// <param name="style">How the diagnostics after the tree render; the <c>parse/</c> goldens use <see cref="Style.Plain"/>.</param>
    public static string Dump(SyntaxTree tree, Style style)
    {
        var w = new Writer();
        w.Line(0, "CompilationUnit");
        foreach (var member in tree.Root.Members) w.Decl(1, member);
        if (tree.Diagnostics.Count > 0)
            w.Raw("\n" + DiagnosticRenderer.RenderAll(tree.Diagnostics, includePath: false, style));
        return w.ToString();
    }

    public static string Expr(Expr expr)
    {
        var sb = new StringBuilder();
        Writer.Expr(sb, expr);
        return sb.ToString();
    }

    public static string Type(TypeSyntax type)
    {
        var sb = new StringBuilder();
        Writer.Type(sb, type);
        return sb.ToString();
    }

    private sealed class Writer
    {
        private readonly StringBuilder _sb = new();

        public override string ToString() => _sb.ToString();

        public void Raw(string text) => _sb.Append(text);

        public void Line(int depth, string text) => _sb.Append(' ', depth * 2).Append(text).Append('\n');

        private static string Mods(Modifiers modifiers)
        {
            if (modifiers == Modifiers.None) return "";
            var parts = new List<string>();
            foreach (var flag in Enum.GetValues<Modifiers>())
                if (flag != Modifiers.None && (modifiers & flag) != 0) parts.Add(flag.ToString().ToLowerInvariant());
            return string.Join(' ', parts) + " ";
        }

        private static string TypeParams(Name[] names) =>
            names.Length == 0 ? "" : "<" + string.Join(", ", names.Select(n => n.Text)) + ">";

        private static string Params(Param[] parameters, bool refThis = false)
        {
            var sb = new StringBuilder(refThis ? "(ref this" : "(");
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0 || refThis) sb.Append(", ");
                var p = parameters[i];
                if (p.Modifier != ParamModifier.None) sb.Append(p.Modifier.ToString().ToLowerInvariant()).Append(' ');
                Type(sb, p.Type);
                sb.Append(' ').Append(p.Name.Text);
                if (p.Default is not null)
                {
                    sb.Append(" = ");
                    Expr(sb, p.Default);
                }
            }
            return sb.Append(')').ToString();
        }

        private void Doc(int depth, Decl decl)
        {
            if (decl.Doc is null) return;
            Line(depth, "doc " + TokenDump.Escape(decl.Doc));
        }

        private static string Attribute(AttributeSyntax a) =>
            $"[{a.Name.Text}" + string.Concat(a.Args.Select(arg => " " + (arg.Name is null ? "" : arg.Name.Text + ": ") + AstDump.Expr(arg.Value))) + "]";

        public void Decl(int depth, Decl decl)
        {
            Doc(depth, decl);
            foreach (var a in decl.Attributes) Line(depth, "Attribute " + Attribute(a));
            switch (decl)
            {
                case ImportDecl d:
                    Line(depth, "Import " + string.Join('.', d.Path.Select(p => p.Text)) + (d.Alias is null ? "" : " as " + d.Alias.Text));
                    break;
                case GlobalStatement d:
                    Stmt(depth, d.Statement);
                    break;
                case FunctionDecl d:
                    {
                        var head = $"Function {Mods(d.Modifiers)}{d.Name.Text}{TypeParams(d.TypeParams)}{Params(d.Params, d.RefThis is not null)} -> {Type(d.ReturnType)}";
                        if (d.Fails is not null)
                            head += d.Fails.Errors.Length == 0 ? " fails" : " fails " + string.Join(", ", d.Fails.Errors.Select(Type));
                        foreach (var r in d.Requires) head += " requires " + AstDump.Expr(r);
                        if (d.ExpressionBody is not null) head += " => " + AstDump.Expr(d.ExpressionBody);
                        Line(depth, head);
                        if (d.Body is not null) Stmt(depth + 1, d.Body);
                        break;
                    }
                case StructDecl d:
                    Line(depth, $"Struct {Mods(d.Modifiers)}{d.Name.Text}{TypeParams(d.TypeParams)}");
                    foreach (var member in d.Members) Decl(depth + 1, member);
                    break;
                case FieldDecl d:
                    Line(depth, $"Field {Mods(d.Modifiers)}{Type(d.Type)} {d.Name.Text}" + (d.Default is null ? "" : " = " + AstDump.Expr(d.Default)));
                    break;
                case PropertyDecl d:
                    Line(depth, $"Property {Mods(d.Modifiers)}{Type(d.Type)} {d.Name.Text} => {AstDump.Expr(d.Body)}");
                    break;
                case ConstructorDecl d:
                    Line(depth, $"Constructor {Mods(d.Modifiers)}{d.Name.Text}{Params(d.Params)}");
                    Stmt(depth + 1, d.Body);
                    break;
                case ErrorEnumDecl d:
                    Line(depth, $"ErrorEnum {Mods(d.Modifiers)}{d.Name.Text}");
                    foreach (var v in d.Variants)
                        Line(depth + 1, "Variant " + v.Name.Text + string.Concat(v.Attributes.Select(a => " " + Attribute(a)))
                            + (v.Doc is null ? "" : " doc " + TokenDump.Escape(v.Doc)));
                    break;
                case EnumDecl d:
                    Line(depth, $"Enum {Mods(d.Modifiers)}{d.Name.Text}{(d.Params is null ? "" : Params(d.Params))}");
                    foreach (var m in d.Members)
                        Line(depth + 1, "Member " + m.Name.Text
                            + (m.Args is null ? "" : "(" + string.Join(", ", m.Args.Select(a => (a.Name is null ? "" : a.Name.Text + ": ") + AstDump.Expr(a.Value))) + ")")
                            + (m.Doc is null ? "" : " doc " + TokenDump.Escape(m.Doc)));
                    break;
                case UnionDecl d:
                    Line(depth, $"Union {Mods(d.Modifiers)}{d.Name.Text}{TypeParams(d.TypeParams)}");
                    foreach (var v in d.Variants)
                        Line(depth + 1, "Variant " + v.Name.Text + (v.Fields.Length == 0 ? "" : "(" + string.Join(", ", v.Fields.Select(f => Type(f.Type) + " " + f.Name.Text)) + ")")
                            + (v.Value is null ? "" : " = " + AstDump.Expr(v.Value))
                            + (v.Doc is null ? "" : " doc " + TokenDump.Escape(v.Doc)));
                    break;
                case TypeAliasDecl d:
                    Line(depth, $"TypeAlias {Mods(d.Modifiers)}{d.Name.Text} = {Type(d.Target)}");
                    break;
                case ErrorSetDecl d:
                    Line(depth, $"ErrorSet {Mods(d.Modifiers)}{d.Name.Text} = {string.Join(", ", d.Members.Select(Type))}");
                    break;
                case TestDecl d:
                    Line(depth, "Test " + TokenDump.Escape(d.Title) + (d.Params.Length == 0 ? "" : " " + Params(d.Params)));
                    Stmt(depth + 1, d.Body);
                    break;
                default:
                    Line(depth, decl.GetType().Name);
                    break;
            }
        }

        public void Stmt(int depth, Stmt stmt)
        {
            switch (stmt)
            {
                case BlockStmt s:
                    Line(depth, "Block");
                    foreach (var child in s.Statements) Stmt(depth + 1, child);
                    break;
                case LocalDecl s:
                    {
                        var head = (s.Using ? "Using " : "") + s.Kind + " " + s.Name.Text;
                        if (s.Type is not null) head += ": " + Type(s.Type);
                        if (s.Init is not null) head += " = " + AstDump.Expr(s.Init);
                        Line(depth, head);
                        break;
                    }
                case ExprStmt s:
                    Line(depth, "Expr " + AstDump.Expr(s.Expr));
                    break;
                case AssignStmt s:
                    Line(depth, $"Assign {AstDump.Expr(s.Target)} {AssignText(s.Op)} {AstDump.Expr(s.Value)}");
                    break;
                case IncDecStmt s:
                    Line(depth, (s.Increment ? "Inc " : "Dec ") + AstDump.Expr(s.Target));
                    break;
                case ReturnStmt s:
                    Line(depth, "Return" + (s.Value is null ? "" : " " + AstDump.Expr(s.Value)));
                    break;
                case BreakStmt s:
                    Line(depth, "Break" + (s.Label is null ? "" : " " + s.Label.Text));
                    break;
                case ContinueStmt s:
                    Line(depth, "Continue" + (s.Label is null ? "" : " " + s.Label.Text));
                    break;
                case IfStmt s:
                    Line(depth, "If " + s.Likelihood switch { Likelihood.Unlikely => "unlikely ", Likelihood.Never => "never ", _ => "" } + AstDump.Expr(s.Condition));
                    Stmt(depth + 1, s.Then);
                    if (s.Else is not null)
                    {
                        Line(depth, "Else");
                        Stmt(depth + 1, s.Else);
                    }
                    break;
                case UnlessStmt s:
                    Line(depth, "Unless " + AstDump.Expr(s.Condition));
                    Stmt(depth + 1, s.Body);
                    break;
                case GuardedStmt s:
                    Line(depth, (s.Unless ? "Guarded unless " : s.Unlikely ? "Guarded if unlikely " : "Guarded if ") + AstDump.Expr(s.Condition));
                    Stmt(depth + 1, s.Statement);
                    break;
                case WhileStmt s:
                    Line(depth, "While " + AstDump.Expr(s.Condition));
                    Stmt(depth + 1, s.Body);
                    break;
                case WhileLetStmt s:
                    Line(depth, $"WhileLet {s.Variable.Text} = " + AstDump.Expr(s.Value));
                    Stmt(depth + 1, s.Body);
                    break;
                case DoWhileStmt s:
                    Line(depth, "DoWhile " + AstDump.Expr(s.Condition));
                    Stmt(depth + 1, s.Body);
                    break;
                case ForStmt s:
                    Line(depth, (s.Try ? "For try " : "For ") + $"{s.Kind} "
                        + (s.Names.Length > 0 ? "(" + string.Join(", ", s.Names.Select(n => n.Text)) + ")" : s.Variable.Text) + (s.Type is null ? "" : ": " + Type(s.Type)) + " in " + AstDump.Expr(s.Iterable));
                    Stmt(depth + 1, s.Body);
                    break;
                case LabeledStmt s:
                    Line(depth, "Label " + s.Label.Text);
                    Stmt(depth + 1, s.Statement);
                    break;
                case SwitchStmt s:
                    Line(depth, "Switch " + AstDump.Expr(s.Value));
                    foreach (var section in s.Sections)
                    {
                        var label = section.Label.Pattern is null
                            ? "Default"
                            : "Case " + Pattern(section.Label.Pattern) + (section.Label.Guard is null ? "" : " when " + AstDump.Expr(section.Label.Guard));
                        Line(depth + 1, label);
                        foreach (var child in section.Statements) Stmt(depth + 2, child);
                    }
                    break;
                case DeferStmt s:
                    Line(depth, "Defer" + s.Kind switch { DeferKind.Failing => " failing", DeferKind.Panicking => " panicking", _ => "" }
                        + (s.Binding is null ? "" : $" ({s.Binding.Text})"));
                    Stmt(depth + 1, s.Body);
                    break;
                case ContextStmt s:
                    Line(depth, "Context " + string.Join(", ", s.Values.Select(v => v.Name.Text + ": " + AstDump.Expr(v.Value))));
                    Stmt(depth + 1, s.Body);
                    break;
                case EmptyStmt:
                    Line(depth, "Empty");
                    break;
                case ErrorStmt:
                    Line(depth, "<error>");
                    break;
                default:
                    Line(depth, stmt.GetType().Name);
                    break;
            }
        }

        private static string AssignText(AssignOp op) => op switch
        {
            AssignOp.Assign => "=",
            AssignOp.Add => "+=",
            AssignOp.Sub => "-=",
            AssignOp.Mul => "*=",
            AssignOp.Div => "/=",
            AssignOp.Rem => "%=",
            AssignOp.And => "&=",
            AssignOp.Or => "|=",
            AssignOp.Xor => "^=",
            AssignOp.Shl => "<<=",
            AssignOp.Shr => ">>=",
            AssignOp.Coalesce => "??=",
            _ => op.ToString(),
        };

        public static string BinaryText(BinaryOp op) => op switch
        {
            BinaryOp.Add => "+",
            BinaryOp.Sub => "-",
            BinaryOp.Mul => "*",
            BinaryOp.Div => "/",
            BinaryOp.Rem => "%",
            BinaryOp.AddWrap => "+%",
            BinaryOp.SubWrap => "-%",
            BinaryOp.MulWrap => "*%",
            BinaryOp.AddSat => "+|",
            BinaryOp.SubSat => "-|",
            BinaryOp.MulSat => "*|",
            BinaryOp.Shl => "<<",
            BinaryOp.Shr => ">>",
            BinaryOp.Lt => "<",
            BinaryOp.Gt => ">",
            BinaryOp.Le => "<=",
            BinaryOp.Ge => ">=",
            BinaryOp.Eq => "==",
            BinaryOp.Ne => "!=",
            BinaryOp.BitAnd => "&",
            BinaryOp.BitOr => "|",
            BinaryOp.BitXor => "^",
            BinaryOp.LogicalAnd => "&&",
            BinaryOp.LogicalOr => "||",
            BinaryOp.Coalesce => "??",
            _ => op.ToString(),
        };

        private static string UnaryText(UnaryOp op) => op switch
        {
            UnaryOp.Plus => "+",
            UnaryOp.Negate => "-",
            UnaryOp.Not => "!",
            UnaryOp.Complement => "~",
            UnaryOp.FromEnd => "^",
            _ => op.ToString(),
        };

        public static string Type(TypeSyntax type)
        {
            var sb = new StringBuilder();
            Type(sb, type);
            return sb.ToString();
        }

        public static void Type(StringBuilder sb, TypeSyntax type)
        {
            switch (type)
            {
                case NamedType t:
                    sb.Append(string.Join('.', t.Path.Select(p => p.Text)));
                    if (t.Args.Length > 0) sb.Append('<').Append(string.Join(", ", t.Args.Select(Type))).Append('>');
                    break;
                case OptionalType t:
                    Type(sb, t.Inner);
                    sb.Append('?');
                    break;
                case ArrayType t:
                    Type(sb, t.Element);
                    sb.Append('[');
                    Expr(sb, t.Size);
                    sb.Append(']');
                    break;
                case TupleType t:
                    sb.Append('(').Append(string.Join(", ", t.Elements.Select(e => Type(e.Type) + (e.Name is null ? "" : " " + e.Name.Text)))).Append(')');
                    break;
            }
        }

        private static string Pattern(Pattern pattern)
        {
            var sb = new StringBuilder();
            Pattern(sb, pattern);
            return sb.ToString();
        }

        private static void Pattern(StringBuilder sb, Pattern pattern)
        {
            switch (pattern)
            {
                case ConstantPattern p:
                    Expr(sb, p.Value);
                    break;
                case RelationalPattern p:
                    sb.Append('(').Append(BinaryText(p.Op)).Append(' ');
                    Expr(sb, p.Value);
                    sb.Append(')');
                    break;
                case VarPattern p:
                    sb.Append(p.IsLet ? "(let " : "(var ").Append(p.Name.Text).Append(')');
                    break;
                case DiscardPattern:
                    sb.Append('_');
                    break;
                case DeclarationPattern p:
                    sb.Append("(decl ").Append(Type(p.Type)).Append(' ').Append(p.Name.Text).Append(')');
                    break;
                case NotPattern p:
                    sb.Append("(not ");
                    Pattern(sb, p.Operand);
                    sb.Append(')');
                    break;
                case BinaryPattern p:
                    sb.Append(p.IsAnd ? "(and " : "(or ");
                    Pattern(sb, p.Left);
                    sb.Append(' ');
                    Pattern(sb, p.Right);
                    sb.Append(')');
                    break;
                case PropertyPattern p:
                    sb.Append("(props");
                    if (p.Type is not null) sb.Append(' ').Append(Type(p.Type));
                    foreach (var sub in p.Properties)
                    {
                        sb.Append(" (").Append(string.Join('.', sub.Path.Select(n => n.Text))).Append(": ");
                        Pattern(sb, sub.Pattern);
                        sb.Append(')');
                    }
                    sb.Append(')');
                    if (p.Designation is not null) sb.Append(' ').Append(p.Designation.Text);
                    break;
                case PositionalPattern p:
                    sb.Append("(pos");
                    if (p.Type is not null) sb.Append(' ').Append(Type(p.Type));
                    foreach (var e in p.Elements)
                    {
                        sb.Append(' ');
                        Pattern(sb, e);
                    }
                    sb.Append(')');
                    break;
                case VariantPattern p:
                    sb.Append("(variant .").Append(p.Variant.Member.Text);
                    foreach (var e in p.Elements)
                    {
                        sb.Append(' ');
                        Pattern(sb, e);
                    }
                    sb.Append(')');
                    break;
                case SlicePattern p:
                    sb.Append("(slice");
                    if (p.Inner is not null)
                    {
                        sb.Append(' ');
                        Pattern(sb, p.Inner);
                    }
                    sb.Append(')');
                    break;
                case ListPattern p:
                    sb.Append("(listp");
                    foreach (var e in p.Elements)
                    {
                        sb.Append(' ');
                        Pattern(sb, e);
                    }
                    sb.Append(')');
                    break;
            }
        }

        private static void Sexp(StringBuilder sb, string head, params Expr?[] items)
        {
            sb.Append('(').Append(head);
            foreach (var item in items)
            {
                sb.Append(' ');
                if (item is null) sb.Append('_');
                else Expr(sb, item);
            }
            sb.Append(')');
        }

        public static void Expr(StringBuilder sb, Expr expr)
        {
            switch (expr)
            {
                case LiteralExpr e:
                    switch (e.Kind)
                    {
                        case LiteralKind.String:
                        case LiteralKind.RawString:
                            sb.Append(Quote(e.Text));
                            break;
                        case LiteralKind.CString:
                            sb.Append('c').Append(Quote(e.Text));
                            break;
                        case LiteralKind.U16String:
                            sb.Append("u16").Append(Quote(e.Text));
                            break;
                        default:
                            sb.Append(e.Text);
                            if (e.Suffix is not null) sb.Append(e.Suffix.Text);
                            break;
                    }
                    break;
                case InterpolatedExpr e:
                    sb.Append("(interp");
                    foreach (var part in e.Parts)
                    {
                        sb.Append(' ');
                        if (part is InterpolationText t) sb.Append(Quote(t.Text));
                        else Expr(sb, ((InterpolationHole)part).Expr);
                    }
                    sb.Append(')');
                    break;
                case NameExpr e:
                    sb.Append(e.Name.Text);
                    if (e.TypeArgs.Length > 0) sb.Append('<').Append(string.Join(", ", e.TypeArgs.Select(Type))).Append('>');
                    break;
                case ThisExpr:
                    sb.Append("this");
                    break;
                case MemberExpr e:
                    sb.Append(e.NullConditional ? "(?. " : "(. ");
                    Expr(sb, e.Target);
                    sb.Append(' ').Append(e.Member.Text);
                    if (e.TypeArgs.Length > 0) sb.Append('<').Append(string.Join(", ", e.TypeArgs.Select(Type))).Append('>');
                    sb.Append(')');
                    break;
                case TargetMemberExpr e:
                    sb.Append('.').Append(e.Member.Text);
                    break;
                case CallExpr e:
                    sb.Append("(call ");
                    Expr(sb, e.Callee);
                    foreach (var arg in e.Args)
                    {
                        sb.Append(' ');
                        var wrapped = arg.Name is not null || arg.Modifier != ArgModifier.None;
                        if (wrapped) sb.Append('(');
                        if (arg.Name is not null) sb.Append(arg.Name.Text).Append(": ");
                        if (arg.Modifier != ArgModifier.None) sb.Append(arg.Modifier.ToString().ToLowerInvariant()).Append(' ');
                        Expr(sb, arg.Value);
                        if (wrapped) sb.Append(')');
                    }
                    sb.Append(')');
                    break;
                case IndexExpr e:
                    Sexp(sb, "index", e.Target, e.Index);
                    break;
                case UnaryExpr e:
                    Sexp(sb, UnaryText(e.Op), e.Operand);
                    break;
                case BinaryExpr e:
                    Sexp(sb, BinaryText(e.Op), e.Left, e.Right);
                    break;
                case RangeExpr e:
                    Sexp(sb, e.Inclusive ? "..=" : "..", e.Start, e.End);
                    break;
                case InExpr e:
                    Sexp(sb, "in", e.Value, e.Range);
                    break;
                case ConditionalExpr e:
                    Sexp(sb, "?", e.Condition, e.WhenTrue, e.WhenFalse);
                    break;
                case CastExpr e:
                    sb.Append("(cast ").Append(Type(e.Type)).Append(' ');
                    Expr(sb, e.Operand);
                    sb.Append(')');
                    break;
                case IsExpr e:
                    sb.Append("(is ");
                    Expr(sb, e.Value);
                    sb.Append(' ');
                    Pattern(sb, e.Pattern);
                    sb.Append(')');
                    break;
                case SwitchExpr e:
                    sb.Append("(switch ");
                    Expr(sb, e.Value);
                    foreach (var arm in e.Arms)
                    {
                        sb.Append(" (");
                        Pattern(sb, arm.Pattern);
                        if (arm.Guard is not null)
                        {
                            sb.Append(" when ");
                            Expr(sb, arm.Guard);
                        }
                        sb.Append(" => ");
                        Expr(sb, arm.Value);
                        sb.Append(')');
                    }
                    sb.Append(')');
                    break;
                case TryExpr e:
                    Sexp(sb, "try", e.Operand);
                    break;
                case MoveExpr e:
                    Sexp(sb, "move", e.Operand);
                    break;
                case CatchExpr e:
                    sb.Append("(catch ");
                    Expr(sb, e.Operand);
                    if (e.Binding is not null) sb.Append(" (").Append(e.Binding.Text).Append(')');
                    if (e.Variant is not null)
                    {
                        sb.Append(" (variant ");
                        Expr(sb, e.Variant);
                        sb.Append(')');
                    }
                    sb.Append(' ');
                    Expr(sb, e.Handler);
                    sb.Append(')');
                    break;
                case BlockExpr e:
                    sb.Append("(block");
                    foreach (var s in e.Block.Statements)
                    {
                        // Statements dump one per line; inside an expression each becomes `{…}`, nested lines joined by `; `.
                        var w = new Writer();
                        w.Stmt(0, s);
                        sb.Append(" {").Append(string.Join("; ", w.ToString().TrimEnd('\n').Split('\n').Select(l => l.Trim()))).Append('}');
                    }
                    sb.Append(')');
                    break;
                case JumpExpr e:
                    switch (e.Jump)
                    {
                        case ReturnStmt r:
                            Sexp(sb, "return", r.Value is null ? [] : [r.Value]);
                            break;
                        case BreakStmt b:
                            sb.Append("(break").Append(b.Label is null ? "" : " " + b.Label.Text).Append(')');
                            break;
                        case ContinueStmt c:
                            sb.Append("(continue").Append(c.Label is null ? "" : " " + c.Label.Text).Append(')');
                            break;
                    }
                    break;
                case StructLiteralExpr e:
                    sb.Append("(struct ").Append(e.Type is null ? "_" : Type(e.Type));
                    FieldInits(sb, e.Fields);
                    sb.Append(')');
                    break;
                case WithExpr e:
                    sb.Append("(with ");
                    Expr(sb, e.Target);
                    FieldInits(sb, e.Fields);
                    sb.Append(')');
                    break;
                case SpreadExpr e:
                    Sexp(sb, "spread", e.Operand);
                    break;
                case MapEntryExpr e:
                    Sexp(sb, "entry", e.Key, e.Value);
                    break;
                case CollectionExpr e:
                    Sexp(sb, e.Elements.Length > 0 && e.Elements.All(x => x is MapEntryExpr) ? "map" : "list", e.Elements);
                    break;
                case TupleExpr e:
                    sb.Append("(tuple");
                    foreach (var element in e.Elements)
                    {
                        sb.Append(' ');
                        if (element.Name is not null)
                        {
                            sb.Append('(').Append(element.Name.Text).Append(": ");
                            Expr(sb, element.Value);
                            sb.Append(')');
                        }
                        else
                        {
                            Expr(sb, element.Value);
                        }
                    }
                    sb.Append(')');
                    break;
                case ParenExpr e:
                    Sexp(sb, "paren", e.Inner);
                    break;
                case StackallocExpr e:
                    sb.Append("(stackalloc ").Append(Type(e.Element)).Append(' ');
                    Expr(sb, e.Count);
                    sb.Append(')');
                    break;
                case CaughtErrorExpr:
                    sb.Append("(caught)");
                    break;
                case ErrorExpr:
                    sb.Append("<error>");
                    break;
                default:
                    sb.Append(expr.GetType().Name);
                    break;
            }
        }

        private static void FieldInits(StringBuilder sb, FieldInit[] fields)
        {
            foreach (var field in fields)
            {
                sb.Append(" (").Append(field.Name.Text).Append(' ');
                Expr(sb, field.Value);
                sb.Append(')');
            }
        }

        private static string Quote(string text)
        {
            var escaped = TokenDump.Escape(text);
            return "\"" + escaped[1..^1].Replace("\"", "\\\"") + "\"";
        }
    }
}
