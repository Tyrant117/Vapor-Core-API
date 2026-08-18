#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Vapor.Inspector
{
    /// <summary>
    /// Turns a parsed resolver into a LINQ expression tree ready to compile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Binding is where the grammar meets the inspected type, so it is also where every "that name means
    /// nothing here" diagnostic comes from. Every failure is a <see cref="ResolverSyntaxException"/>
    /// carrying the column of the offending token, because the author's next question is always which
    /// part of the string was wrong.
    /// </para>
    /// <para>
    /// One deliberate departure from C#: <c>?.</c> treats a destroyed <see cref="Object"/> as null. C#
    /// only checks the managed reference, so <c>@Target?.name</c> on a destroyed object would pass the
    /// check and then throw from inside Unity. In an inspector the useful reading of "is this thing
    /// there" is Unity's, and the C# reading is never what the author meant.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    internal static class ResolverBinder
    {
        /// <summary>
        /// The types whose static members a resolver may name. Deliberately a closed list: opening this
        /// up to every loaded type would make a bare <c>Color</c> ambiguous between however many types
        /// share the name across assemblies, and the resolver has no <c>using</c> directives to break
        /// the tie with.
        /// </summary>
        private static readonly Dictionary<string, Type> s_StaticTypes = new(StringComparer.Ordinal)
        {
            { "Color", typeof(Color) },
            { "Color32", typeof(Color32) },
            { "Vector2", typeof(Vector2) },
            { "Vector3", typeof(Vector3) },
            { "Vector4", typeof(Vector4) },
            { "Vector2Int", typeof(Vector2Int) },
            { "Vector3Int", typeof(Vector3Int) },
            { "Quaternion", typeof(Quaternion) },
            { "Rect", typeof(Rect) },
            { "Bounds", typeof(Bounds) },
            { "Mathf", typeof(Mathf) },
            { "Math", typeof(Math) },

            { "int", typeof(int) },
            { "Int32", typeof(int) },
            { "uint", typeof(uint) },
            { "long", typeof(long) },
            { "short", typeof(short) },
            { "byte", typeof(byte) },
            { "sbyte", typeof(sbyte) },
            { "float", typeof(float) },
            { "Single", typeof(float) },
            { "double", typeof(double) },
            { "decimal", typeof(decimal) },
            { "bool", typeof(bool) },
            { "char", typeof(char) },
            { "string", typeof(string) },
        };

        /// <summary>
        /// Widening order for the numeric promotions the comparison operators need. Anything absent is
        /// not a number as far as this grammar is concerned.
        /// </summary>
        private static readonly Dictionary<Type, int> s_NumericRank = new()
        {
            { typeof(byte), 1 },
            { typeof(sbyte), 1 },
            { typeof(short), 2 },
            { typeof(ushort), 2 },
            { typeof(char), 2 },
            { typeof(int), 3 },
            { typeof(uint), 4 },
            { typeof(long), 5 },
            { typeof(ulong), 6 },
            { typeof(float), 7 },
            { typeof(double), 8 },
            { typeof(decimal), 9 },
        };

        private static readonly MethodInfo s_UnityObjectEquality =
            typeof(Object).GetMethod("op_Equality", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(Object), typeof(Object) }, null);

        private sealed class Context
        {
            public ParameterExpression Target;
            public Type TargetType;
        }

        /// <summary>
        /// Binds <paramref name="root"/> against <paramref name="targetType"/> and returns a lambda that
        /// reads it out of a boxed instance.
        /// </summary>
        /// <remarks>
        /// A null instance yields <c>default</c> rather than throwing. Resolvers are polled continuously
        /// against a tree that can hold an unassigned reference for as long as the author leaves the
        /// field empty, so the null case is ordinary rather than exceptional.
        /// </remarks>
        public static LambdaExpression Bind(ResolverNode root, Type targetType, Type resultType)
        {
            var parameter = Expression.Parameter(typeof(object), "target");
            var context = new Context { Target = parameter, TargetType = targetType };

            var body = Bind(root, context, resultType);
            body = Coerce(body, resultType)
                   ?? throw new ResolverSyntaxException($"The expression is a {Describe(body.Type)} which cannot be read as {Describe(resultType)}.", root.Position);

            var guarded = Expression.Condition(
                Expression.ReferenceEqual(parameter, Expression.Constant(null, typeof(object))),
                Expression.Default(resultType),
                body);

            return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(object), resultType), guarded, parameter);
        }

        #region - Nodes -
        private static Expression Bind(ResolverNode node, Context context, Type hint)
        {
            return node switch
            {
                ResolverLiteralNode literal => BindLiteral(literal, hint),
                ResolverNameNode or ResolverMemberNode => BindChain(node, context, hint),
                ResolverUnaryNode unary => BindUnary(unary, context, hint),
                ResolverBinaryNode binary => BindBinary(binary, context),
                ResolverCoalesceNode coalesce => BindCoalesce(coalesce, context, hint),
                ResolverConditionalNode conditional => BindConditional(conditional, context, hint),
                _ => throw new ResolverSyntaxException("Unsupported expression.", node.Position),
            };
        }

        private static Expression BindLiteral(ResolverLiteralNode literal, Type hint)
        {
            if (literal.Type != null)
            {
                return Expression.Constant(literal.Value, literal.Type);
            }

            // A bare `null` has no type of its own; it takes the one it is being measured against.
            var type = hint ?? typeof(object);
            if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
            {
                throw new ResolverSyntaxException($"'null' is not a valid {Describe(type)}.", literal.Position);
            }

            return Expression.Constant(null, type);
        }

        private static Expression BindUnary(ResolverUnaryNode unary, Context context, Type hint)
        {
            if (unary.Operator == ResolverTokenType.Not)
            {
                var operand = AsBoolean(Bind(unary.Operand, context, typeof(bool)), unary.Position);
                return Expression.Not(operand);
            }

            var value = Bind(unary.Operand, context, hint);
            var numeric = Unwrap(value);
            if (!s_NumericRank.ContainsKey(numeric.Type))
            {
                throw new ResolverSyntaxException($"'-' needs a number, not a {Describe(value.Type)}.", unary.Position);
            }

            return Expression.Negate(numeric);
        }

        private static Expression BindBinary(ResolverBinaryNode binary, Context context)
        {
            if (binary.Operator is ResolverTokenType.AndAlso or ResolverTokenType.OrElse)
            {
                var left = AsBoolean(Bind(binary.Left, context, typeof(bool)), binary.Left.Position);
                var right = AsBoolean(Bind(binary.Right, context, typeof(bool)), binary.Right.Position);
                return binary.Operator == ResolverTokenType.AndAlso
                    ? Expression.AndAlso(left, right)
                    : Expression.OrElse(left, right);
            }

            var (lhs, rhs) = BindOperands(binary, context);

            if (binary.Operator is ResolverTokenType.Equal or ResolverTokenType.NotEqual)
            {
                return BindEquality(binary, lhs, rhs);
            }

            var (a, b) = PromoteNumeric(Unwrap(lhs), Unwrap(rhs), binary.Position, binary.Operator);
            return binary.Operator switch
            {
                ResolverTokenType.Less => Expression.LessThan(a, b),
                ResolverTokenType.Greater => Expression.GreaterThan(a, b),
                ResolverTokenType.LessOrEqual => Expression.LessThanOrEqual(a, b),
                _ => Expression.GreaterThanOrEqual(a, b),
            };
        }

        /// <summary>
        /// Binds both sides of a comparison, letting whichever side resolves on its own supply the type
        /// the other is read against.
        /// </summary>
        /// <remarks>
        /// This is what lets <c>@Mode == Modes.Advanced</c> work without a global type lookup: the left
        /// side binds to an enum-typed member, and <c>Modes</c> is then matched against that enum's own
        /// name. Both orders are tried because the author is equally entitled to write the constant
        /// first, and the left-first attempt is the one whose error is reported if neither works — it is
        /// the more likely of the two to name the real mistake.
        /// </remarks>
        private static (Expression Left, Expression Right) BindOperands(ResolverBinaryNode binary, Context context)
        {
            try
            {
                var left = Bind(binary.Left, context, null);
                return (left, Bind(binary.Right, context, left.Type));
            }
            catch (ResolverSyntaxException leftFirst)
            {
                try
                {
                    var right = Bind(binary.Right, context, null);
                    return (Bind(binary.Left, context, right.Type), right);
                }
                catch (ResolverSyntaxException)
                {
                    throw leftFirst;
                }
            }
        }

        private static Expression BindEquality(ResolverBinaryNode binary, Expression lhs, Expression rhs)
        {
            var equal = binary.Operator == ResolverTokenType.Equal;

            // Unity overloads == on Object so that a destroyed object equals null. Routing through the
            // overload keeps `@Target == null` meaning what it means everywhere else in Unity.
            if (typeof(Object).IsAssignableFrom(NonNullable(lhs.Type)) || typeof(Object).IsAssignableFrom(NonNullable(rhs.Type)))
            {
                var call = Expression.Call(s_UnityObjectEquality, AsUnityObject(lhs, binary.Position), AsUnityObject(rhs, binary.Position));
                return equal ? call : Expression.Not(call);
            }

            var left = lhs;
            var right = rhs;
            if (s_NumericRank.ContainsKey(NonNullable(Unwrap(left).Type)) && s_NumericRank.ContainsKey(NonNullable(Unwrap(right).Type)))
            {
                (left, right) = PromoteNumeric(Unwrap(left), Unwrap(right), binary.Position, binary.Operator);
            }
            else if (left.Type != right.Type)
            {
                var converted = Coerce(right, left.Type) ?? Coerce(left, right.Type);
                if (converted == null)
                {
                    throw new ResolverSyntaxException($"A {Describe(left.Type)} cannot be compared with a {Describe(right.Type)}.", binary.Position);
                }

                if (converted.Type == left.Type)
                {
                    right = converted;
                }
                else
                {
                    left = converted;
                }
            }

            return equal ? Expression.Equal(left, right) : Expression.NotEqual(left, right);
        }

        private static Expression BindCoalesce(ResolverCoalesceNode node, Context context, Type hint)
        {
            var left = Bind(node.Left, context, null);
            var underlying = Nullable.GetUnderlyingType(left.Type);

            if (underlying == null && left.Type.IsValueType)
            {
                throw new ResolverSyntaxException($"'??' needs something that can be null on its left, but that is a {Describe(left.Type)}.", node.Position);
            }

            var right = Bind(node.Right, context, underlying ?? left.Type);
            var resultType = underlying ?? left.Type;

            var coercedRight = Coerce(right, resultType)
                               ?? throw new ResolverSyntaxException($"The right of '??' is a {Describe(right.Type)} which cannot be read as {Describe(resultType)}.", node.Right.Position);

            // Evaluated into a local so a method call on the left does not run twice.
            var temp = Expression.Variable(left.Type, "coalesce");
            var value = underlying != null
                ? (Expression)Expression.Property(temp, "Value")
                : temp;

            return Expression.Block(
                new[] { temp },
                Expression.Assign(temp, left),
                Expression.Condition(IsNull(temp), coercedRight, Coerce(value, resultType) ?? value));
        }

        private static Expression BindConditional(ResolverConditionalNode node, Context context, Type hint)
        {
            var condition = AsBoolean(Bind(node.Condition, context, typeof(bool)), node.Condition.Position);
            var ifTrue = Bind(node.IfTrue, context, hint);
            var ifFalse = Bind(node.IfFalse, context, hint);

            var common = CommonType(ifTrue, ifFalse, hint)
                         ?? throw new ResolverSyntaxException(
                             $"The branches of '?:' are a {Describe(ifTrue.Type)} and a {Describe(ifFalse.Type)}, which have no common type.", node.Position);

            return Expression.Condition(condition, Coerce(ifTrue, common), Coerce(ifFalse, common));
        }

        /// <summary>
        /// Picks the type both branches of a <c>?:</c> can be read as, preferring the requested type when
        /// both already fit it so that <c>@Boss ? Color.red : Color.white</c> stays a Color rather than
        /// widening to object.
        /// </summary>
        private static Type CommonType(Expression a, Expression b, Type hint)
        {
            if (a.Type == b.Type)
            {
                return a.Type;
            }

            if (hint != null && Coerce(a, hint) != null && Coerce(b, hint) != null)
            {
                return hint;
            }

            if (s_NumericRank.TryGetValue(NonNullable(a.Type), out var rankA) && s_NumericRank.TryGetValue(NonNullable(b.Type), out var rankB))
            {
                var wider = rankA >= rankB ? NonNullable(a.Type) : NonNullable(b.Type);
                return Nullable.GetUnderlyingType(a.Type) != null || Nullable.GetUnderlyingType(b.Type) != null
                    ? typeof(Nullable<>).MakeGenericType(wider)
                    : wider;
            }

            if (Coerce(b, a.Type) != null)
            {
                return a.Type;
            }

            return Coerce(a, b.Type) != null ? b.Type : null;
        }
        #endregion

        #region - Chains -
        /// <summary>
        /// Binds <c>A</c>, <c>A.B</c>, <c>A?.B.C</c> and so on.
        /// </summary>
        /// <remarks>
        /// The head is looked up on the inspected object first and only considered as a type name if that
        /// fails, so a member always wins over a type of the same name. That ordering is what keeps the
        /// closed static-type list from ever shadowing something the author actually declared.
        /// </remarks>
        private static Expression BindChain(ResolverNode node, Context context, Type hint)
        {
            var links = Flatten(node, out var head);

            var instance = TryBindHeadAsMember(head, context);
            var next = 0;
            if (instance == null)
            {
                var type = ResolveHeadType(head, links, hint, out next);
                if (type == null)
                {
                    throw new ResolverSyntaxException(DescribeUnknownName(head.Name, context.TargetType, links.Count > 0), head.Position);
                }

                instance = BindStaticMember(type, links[next], hint);
                next++;
            }

            return ApplyLinks(instance, links, next);
        }

        /// <summary>
        /// Rebuilds the chain left-to-right. The parser nests it the other way, which is the wrong end to
        /// start binding from.
        /// </summary>
        private static List<ResolverMemberNode> Flatten(ResolverNode node, out ResolverNameNode head)
        {
            var links = new List<ResolverMemberNode>(4);
            while (node is ResolverMemberNode member)
            {
                links.Add(member);
                node = member.Target;
            }

            links.Reverse();
            head = (ResolverNameNode)node;
            return links;
        }

        private static Expression TryBindHeadAsMember(ResolverNameNode head, Context context)
        {
            var member = FindMember(context.TargetType, head.Name);
            if (member == null)
            {
                return null;
            }

            var instance = IsStatic(member) ? null : Expression.Convert(context.Target, context.TargetType);
            return Access(instance, member, head.Invoked, head.Position);
        }

        /// <summary>
        /// Matches the longest run of leading names that forms a type, so a nested enum written out in
        /// full binds as readily as a top-level one.
        /// </summary>
        private static Type ResolveHeadType(ResolverNameNode head, List<ResolverMemberNode> links, Type hint, out int firstMemberIndex)
        {
            // The last link has to stay behind as the member being read off the type.
            for (var take = links.Count - 1; take >= 0; take--)
            {
                var name = head.Name;
                for (var i = 0; i < take; i++)
                {
                    name += "." + links[i].Name;
                }

                var type = ResolveTypeName(name, hint);
                if (type == null)
                {
                    continue;
                }

                firstMemberIndex = take;
                return type;
            }

            firstMemberIndex = 0;
            return null;
        }

        private static Type ResolveTypeName(string name, Type hint)
        {
            var hinted = NonNullable(hint);
            if (hinted is { IsEnum: true } && NameMatches(hinted, name))
            {
                return hinted;
            }

            return s_StaticTypes.GetValueOrDefault(name);
        }

        private static bool NameMatches(Type type, string name)
        {
            if (string.Equals(type.Name, name, StringComparison.Ordinal))
            {
                return true;
            }

            // Nested types report as Outer+Inner; the author writes Outer.Inner.
            var full = type.FullName?.Replace('+', '.');
            return full != null && (string.Equals(full, name, StringComparison.Ordinal) || full.EndsWith("." + name, StringComparison.Ordinal));
        }

        private static Expression BindStaticMember(Type type, ResolverMemberNode link, Type hint)
        {
            var member = FindMember(type, link.Name);
            if (member == null || !IsStatic(member))
            {
                throw new ResolverSyntaxException($"{Describe(type)} has no static '{link.Name}'.", link.Position);
            }

            return Access(null, member, link.Invoked, link.Position);
        }

        /// <summary>
        /// Walks the rest of the chain, wrapping everything downstream of a <c>?.</c> in its null check so
        /// the guard covers the whole tail rather than one hop, as C# does.
        /// </summary>
        private static Expression ApplyLinks(Expression instance, List<ResolverMemberNode> links, int index)
        {
            if (index >= links.Count)
            {
                return instance;
            }

            var link = links[index];
            if (!link.NullConditional)
            {
                return ApplyLinks(ReadMember(instance, link), links, index + 1);
            }

            var temp = Expression.Variable(instance.Type, "link");
            var tail = ApplyLinks(ReadMember(temp, link), links, index + 1);
            var resultType = Nullable.GetUnderlyingType(tail.Type) == null && tail.Type.IsValueType
                ? typeof(Nullable<>).MakeGenericType(tail.Type)
                : tail.Type;

            return Expression.Block(
                new[] { temp },
                Expression.Assign(temp, instance),
                Expression.Condition(
                    IsNull(temp),
                    Expression.Default(resultType),
                    Expression.Convert(tail, resultType)));
        }

        private static Expression ReadMember(Expression instance, ResolverMemberNode link)
        {
            // A preceding ?. leaves a Nullable behind; the rest of the chain reads through it.
            if (Nullable.GetUnderlyingType(instance.Type) != null)
            {
                instance = Expression.Property(instance, "Value");
            }

            var member = FindMember(instance.Type, link.Name);
            if (member == null)
            {
                throw new ResolverSyntaxException($"{Describe(instance.Type)} has no '{link.Name}'.", link.Position);
            }

            if (IsStatic(member))
            {
                throw new ResolverSyntaxException($"'{link.Name}' is static and cannot be read off a value.", link.Position);
            }

            return Access(instance, member, link.Invoked, link.Position);
        }

        private static Expression Access(Expression instance, MemberInfo member, bool invoked, int position)
        {
            switch (member)
            {
                case PropertyInfo property:
                    if (invoked)
                    {
                        throw new ResolverSyntaxException($"'{property.Name}' is a property, so it is written without '()'.", position);
                    }

                    return Expression.Property(instance, property);
                case FieldInfo field:
                    if (invoked)
                    {
                        throw new ResolverSyntaxException($"'{field.Name}' is a field, so it is written without '()'.", position);
                    }

                    return field.IsLiteral || field.IsStatic && field.IsInitOnly
                        ? Expression.Constant(field.GetValue(null), field.FieldType)
                        : Expression.Field(instance, field);
                case MethodInfo method:
                    return instance == null ? Expression.Call(method) : Expression.Call(instance, method);
                default:
                    throw new ResolverSyntaxException($"'{member.Name}' cannot be read.", position);
            }
        }

        /// <summary>
        /// Finds a readable member by name, most-derived first.
        /// </summary>
        /// <remarks>
        /// Non-public members are in scope on purpose: a <c>[SerializeField] private bool</c> is exactly
        /// the sort of thing a <c>ShowIf</c> keys off, and requiring the author to make it public to
        /// point at it would be a strange tax.
        /// </remarks>
        private static MemberInfo FindMember(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null; t = t.BaseType)
            {
                var property = t.GetProperty(name, flags);
                if (property is { CanRead: true } && property.GetIndexParameters().Length == 0)
                {
                    return property;
                }

                var method = t.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType != typeof(void))
                {
                    return method;
                }

                var field = t.GetField(name, flags);
                if (field != null)
                {
                    return field;
                }

                if (t == typeof(object))
                {
                    break;
                }
            }

            return null;
        }

        private static bool IsStatic(MemberInfo member) => member switch
        {
            FieldInfo field => field.IsStatic,
            PropertyInfo property => property.GetMethod.IsStatic,
            MethodInfo method => method.IsStatic,
            _ => false,
        };
        #endregion

        #region - Types -
        /// <summary>
        /// Reads <paramref name="value"/> as <paramref name="target"/>, or null when it cannot be. Callers
        /// use the null to decide between trying another conversion and reporting a failure.
        /// </summary>
        private static Expression Coerce(Expression value, Type target)
        {
            if (value.Type == target)
            {
                return value;
            }

            var valueUnderlying = Nullable.GetUnderlyingType(value.Type);
            if (valueUnderlying == target)
            {
                return Expression.Call(value, value.Type.GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!);
            }

            if (Nullable.GetUnderlyingType(target) == value.Type)
            {
                return Expression.Convert(value, target);
            }

            if (target.IsAssignableFrom(value.Type))
            {
                return Expression.Convert(value, target);
            }

            // A nullable source reaching a different target has to lose the wrapper first, and the value
            // it holds when empty is the same default the caller would have seen anyway.
            if (valueUnderlying != null)
            {
                return Coerce(Expression.Call(value, value.Type.GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!), target);
            }

            var from = value.Type.IsEnum ? Enum.GetUnderlyingType(value.Type) : value.Type;
            var to = target.IsEnum ? Enum.GetUnderlyingType(target) : target;
            if (s_NumericRank.TryGetValue(from, out var fromRank) && s_NumericRank.TryGetValue(to, out var toRank) && toRank >= fromRank)
            {
                return Expression.Convert(value, target);
            }

            return null;
        }

        private static (Expression, Expression) PromoteNumeric(Expression a, Expression b, int position, ResolverTokenType op)
        {
            var typeA = NonNullable(a.Type);
            var typeB = NonNullable(b.Type);

            // Ordering two enums is ordering their underlying values, which is what `Mode > Modes.Basic`
            // means in C# too. Handled before the enum/number case below, which only fires when exactly
            // one side is an enum and would leave two enums matching neither branch.
            if (typeA.IsEnum && typeB.IsEnum)
            {
                a = Expression.Convert(Unwrap(a), Enum.GetUnderlyingType(typeA));
                b = Expression.Convert(Unwrap(b), Enum.GetUnderlyingType(typeB));
                typeA = a.Type;
                typeB = b.Type;
            }
            // Comparing an enum against a number is the one place an implicit unwrap earns its keep, and
            // it only ever goes this direction — the number is never read back as an enum.
            else if (typeA.IsEnum && s_NumericRank.ContainsKey(typeB))
            {
                a = Expression.Convert(Unwrap(a), Enum.GetUnderlyingType(typeA));
                typeA = a.Type;
            }
            else if (typeB.IsEnum && s_NumericRank.ContainsKey(typeA))
            {
                b = Expression.Convert(Unwrap(b), Enum.GetUnderlyingType(typeB));
                typeB = b.Type;
            }

            if (!s_NumericRank.TryGetValue(typeA, out var rankA) || !s_NumericRank.TryGetValue(typeB, out var rankB))
            {
                throw new ResolverSyntaxException($"'{Symbol(op)}' needs two numbers, not a {Describe(a.Type)} and a {Describe(b.Type)}.", position);
            }

            var wider = rankA >= rankB ? typeA : typeB;
            return (Expression.Convert(Unwrap(a), wider), Expression.Convert(Unwrap(b), wider));
        }

        /// <summary>Drops a <see cref="Nullable{T}"/> wrapper, substituting the default when empty.</summary>
        private static Expression Unwrap(Expression value)
        {
            return Nullable.GetUnderlyingType(value.Type) == null
                ? value
                : Expression.Call(value, value.Type.GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!);
        }

        private static Expression AsBoolean(Expression value, int position)
        {
            var boolean = Coerce(value, typeof(bool));
            return boolean ?? throw new ResolverSyntaxException($"Expected a true/false value but found a {Describe(value.Type)}.", position);
        }

        private static Expression AsUnityObject(Expression value, int position)
        {
            if (typeof(Object).IsAssignableFrom(value.Type))
            {
                return Expression.Convert(value, typeof(Object));
            }

            // The only non-Object thing that may sit opposite one is a null literal, which BindLiteral
            // will already have typed as whatever the other side was.
            if (!value.Type.IsValueType)
            {
                return Expression.Constant(null, typeof(Object));
            }

            throw new ResolverSyntaxException($"A {Describe(value.Type)} cannot be compared with a Unity object.", position);
        }

        private static Expression IsNull(Expression value)
        {
            if (Nullable.GetUnderlyingType(value.Type) != null)
            {
                return Expression.Not(Expression.Property(value, "HasValue"));
            }

            if (value.Type.IsValueType)
            {
                return Expression.Constant(false);
            }

            return typeof(Object).IsAssignableFrom(value.Type)
                ? Expression.Call(s_UnityObjectEquality, Expression.Convert(value, typeof(Object)), Expression.Constant(null, typeof(Object)))
                : Expression.ReferenceEqual(value, Expression.Constant(null, value.Type));
        }

        private static Type NonNullable(Type type) => type == null ? null : Nullable.GetUnderlyingType(type) ?? type;
        #endregion

        #region - Messages -
        private static string DescribeUnknownName(string name, Type targetType, bool hasMembers)
        {
            var suffix = hasMembers
                ? $" It is not a member of {Describe(targetType)}, and it is not one of the types a resolver can name statically."
                : $" It is not a field, property or parameterless method on {Describe(targetType)}.";
            return $"'{name}' could not be resolved.{suffix}";
        }

        private static string Describe(Type type)
        {
            if (type == null)
            {
                return "null";
            }

            var underlying = Nullable.GetUnderlyingType(type);
            return underlying != null ? Describe(underlying) + "?" : type.Name;
        }

        private static string Symbol(ResolverTokenType op) => op switch
        {
            ResolverTokenType.Less => "<",
            ResolverTokenType.Greater => ">",
            ResolverTokenType.LessOrEqual => "<=",
            ResolverTokenType.GreaterOrEqual => ">=",
            ResolverTokenType.Equal => "==",
            ResolverTokenType.NotEqual => "!=",
            _ => op.ToString(),
        };
        #endregion
    }
}
#endif
