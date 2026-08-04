using System;
using System.Collections.Generic;
using System.Globalization;

namespace Vapor.Serialization
{
    internal static class VslCollection
    {
        /// <summary>
        /// Reads a <c>[ ... ]</c> body into a list. Returns null for an explicit <c>null</c>.
        /// </summary>
        public static List<T> ReadItems<T>(ref VslReader reader, VslContext context)
        {
            if (reader.TryReadNull())
            {
                return null;
            }

            var element = VslFormatterRegistry.Get<T>();
            var items = new List<T>();

            reader.ReadSequenceStart();
            while (reader.TryReadSequenceItem())
            {
                items.Add(element.Read(ref reader, context));
            }

            return items;
        }

        public static bool ShouldInline(IVslFormatter element, int count, VslContext context) =>
            element.IsScalar && count <= context.Options.InlineSequenceLimit;
    }

    public sealed class ArrayFormatter<T> : VslFormatter<T[]>
    {
        private IVslFormatter<T> _element;
        private IVslFormatter<T> Element => _element ??= VslFormatterRegistry.Get<T>();

        public override void Write(ref VslWriter writer, in T[] value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var element = Element;
            writer.BeginSequence(VslCollection.ShouldInline(element, value.Length, context));
            for (var i = 0; i < value.Length; i++)
            {
                element.Write(ref writer, value[i], context);
            }

            writer.EndSequence();
        }

        public override T[] Read(ref VslReader reader, VslContext context) =>
            VslCollection.ReadItems<T>(ref reader, context)?.ToArray();
    }

    public sealed class ListFormatter<T> : VslFormatter<List<T>>
    {
        private IVslFormatter<T> _element;
        private IVslFormatter<T> Element => _element ??= VslFormatterRegistry.Get<T>();

        public override void Write(ref VslWriter writer, in List<T> value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var element = Element;
            writer.BeginSequence(VslCollection.ShouldInline(element, value.Count, context));
            for (var i = 0; i < value.Count; i++)
            {
                element.Write(ref writer, value[i], context);
            }

            writer.EndSequence();
        }

        public override List<T> Read(ref VslReader reader, VslContext context) =>
            VslCollection.ReadItems<T>(ref reader, context);
    }

    public sealed class HashSetFormatter<T> : VslFormatter<HashSet<T>>
    {
        private IVslFormatter<T> _element;
        private IVslFormatter<T> Element => _element ??= VslFormatterRegistry.Get<T>();

        public override void Write(ref VslWriter writer, in HashSet<T> value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var element = Element;
            writer.BeginSequence(VslCollection.ShouldInline(element, value.Count, context));
            foreach (var item in value)
            {
                element.Write(ref writer, item, context);
            }

            writer.EndSequence();
        }

        public override HashSet<T> Read(ref VslReader reader, VslContext context)
        {
            var items = VslCollection.ReadItems<T>(ref reader, context);
            return items == null ? null : new HashSet<T>(items);
        }
    }

    public sealed class QueueFormatter<T> : VslFormatter<Queue<T>>
    {
        private IVslFormatter<T> _element;
        private IVslFormatter<T> Element => _element ??= VslFormatterRegistry.Get<T>();

        public override void Write(ref VslWriter writer, in Queue<T> value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var element = Element;
            writer.BeginSequence(VslCollection.ShouldInline(element, value.Count, context));
            foreach (var item in value)
            {
                element.Write(ref writer, item, context);
            }

            writer.EndSequence();
        }

        public override Queue<T> Read(ref VslReader reader, VslContext context)
        {
            var items = VslCollection.ReadItems<T>(ref reader, context);
            return items == null ? null : new Queue<T>(items);
        }
    }

    public sealed class StackFormatter<T> : VslFormatter<Stack<T>>
    {
        private IVslFormatter<T> _element;
        private IVslFormatter<T> Element => _element ??= VslFormatterRegistry.Get<T>();

        public override void Write(ref VslWriter writer, in Stack<T> value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var element = Element;
            writer.BeginSequence(VslCollection.ShouldInline(element, value.Count, context));
            foreach (var item in value)
            {
                element.Write(ref writer, item, context);
            }

            writer.EndSequence();
        }

        public override Stack<T> Read(ref VslReader reader, VslContext context)
        {
            var items = VslCollection.ReadItems<T>(ref reader, context);
            if (items == null)
            {
                return null;
            }

            // A stack enumerates top-first, so pushing in document order would invert it. Push from
            // the back to land the original top back on top.
            var stack = new Stack<T>(items.Count);
            for (var i = items.Count - 1; i >= 0; i--)
            {
                stack.Push(items[i]);
            }

            return stack;
        }
    }

    public sealed class LinkedListFormatter<T> : VslFormatter<LinkedList<T>>
    {
        private IVslFormatter<T> _element;
        private IVslFormatter<T> Element => _element ??= VslFormatterRegistry.Get<T>();

        public override void Write(ref VslWriter writer, in LinkedList<T> value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var element = Element;
            writer.BeginSequence(VslCollection.ShouldInline(element, value.Count, context));
            foreach (var item in value)
            {
                element.Write(ref writer, item, context);
            }

            writer.EndSequence();
        }

        public override LinkedList<T> Read(ref VslReader reader, VslContext context)
        {
            var items = VslCollection.ReadItems<T>(ref reader, context);
            return items == null ? null : new LinkedList<T>(items);
        }
    }

    public sealed class KeyValuePairFormatter<TKey, TValue> : VslFormatter<KeyValuePair<TKey, TValue>>
    {
        private IVslFormatter<TKey> _key;
        private IVslFormatter<TValue> _value;
        private IVslFormatter<TKey> Key => _key ??= VslFormatterRegistry.Get<TKey>();
        private IVslFormatter<TValue> Value => _value ??= VslFormatterRegistry.Get<TValue>();

        public override bool IsScalar => Key.IsScalar && Value.IsScalar;

        public override void Write(ref VslWriter writer, in KeyValuePair<TKey, TValue> value, VslContext context)
        {
            writer.BeginTuple();
            Key.Write(ref writer, value.Key, context);
            Value.Write(ref writer, value.Value, context);
            writer.EndTuple();
        }

        public override KeyValuePair<TKey, TValue> Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var key = reader.AtEnd() ? default : Key.Read(ref reader, context);
            var value = reader.AtEnd() ? default : Value.Read(ref reader, context);
            reader.ReadTupleEnd();
            return new KeyValuePair<TKey, TValue>(key, value);
        }
    }

    /// <summary>
    /// Writes a dictionary as an object when its keys read as names — <c>{ fire: 1  ice: 2 }</c> —
    /// and as a sequence of pairs otherwise.
    /// </summary>
    /// <remarks>
    /// The object form is what makes <c>Dictionary&lt;string, T&gt;</c> and enum-keyed lookups
    /// legible; anything else would force a reader to decode positional pairs.
    /// </remarks>
    public sealed class DictionaryFormatter<TKey, TValue> : VslFormatter<Dictionary<TKey, TValue>>
    {
        private static readonly bool s_NameKeys = IsNameKey(typeof(TKey));

        private IVslFormatter<TKey> _key;
        private IVslFormatter<TValue> _value;
        private IVslFormatter<TKey> Key => _key ??= VslFormatterRegistry.Get<TKey>();
        private IVslFormatter<TValue> Value => _value ??= VslFormatterRegistry.Get<TValue>();

        public override void Write(ref VslWriter writer, in Dictionary<TKey, TValue> value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var valueFormatter = Value;

            if (s_NameKeys)
            {
                writer.BeginObject(valueFormatter.IsScalar && value.Count <= context.Options.InlineMemberLimit);
                foreach (var pair in value)
                {
                    writer.WriteMember(KeyToName(pair.Key));
                    valueFormatter.Write(ref writer, pair.Value, context);
                }

                writer.EndObject();
                return;
            }

            var keyFormatter = Key;
            writer.BeginSequence(keyFormatter.IsScalar && valueFormatter.IsScalar &&
                                 value.Count <= context.Options.InlineSequenceLimit);
            foreach (var pair in value)
            {
                writer.BeginTuple();
                keyFormatter.Write(ref writer, pair.Key, context);
                valueFormatter.Write(ref writer, pair.Value, context);
                writer.EndTuple();
            }

            writer.EndSequence();
        }

        public override Dictionary<TKey, TValue> Read(ref VslReader reader, VslContext context)
        {
            if (reader.TryReadNull())
            {
                return null;
            }

            var result = new Dictionary<TKey, TValue>();
            var valueFormatter = Value;

            // Accept whichever shape is actually present, not just the one we would have written.
            if (reader.PeekKind() == VslValueKind.Object)
            {
                reader.ReadObjectStart();
                while (reader.TryReadMemberName(out var name))
                {
                    if (TryParseKey(name, context, out var key))
                    {
                        result[key] = valueFormatter.Read(ref reader, context);
                    }
                    else
                    {
                        reader.SkipValue();
                    }
                }

                return result;
            }

            var keyFormatter = Key;
            reader.ReadSequenceStart();
            while (reader.TryReadSequenceItem())
            {
                reader.ReadTupleStart();
                var key = reader.AtEnd() ? default : keyFormatter.Read(ref reader, context);
                var value = reader.AtEnd() ? default : valueFormatter.Read(ref reader, context);
                reader.ReadTupleEnd();
                result[key] = value;
            }

            return result;
        }

        private static bool IsNameKey(Type type)
        {
            if (type == typeof(string) || type.IsEnum)
            {
                return true;
            }

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return true;
                default:
                    return false;
            }
        }

        private static string KeyToName(TKey key)
        {
            if (key is string text)
            {
                return text;
            }

            if (key is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return key?.ToString() ?? string.Empty;
        }

        private static bool TryParseKey(ReadOnlySpan<char> text, VslContext context, out TKey key)
        {
            var type = typeof(TKey);
            try
            {
                if (type == typeof(string))
                {
                    key = (TKey)(object)text.ToString();
                    return true;
                }

                if (type.IsEnum)
                {
                    key = (TKey)Enum.Parse(type, text.ToString(), true);
                    return true;
                }

                key = (TKey)Convert.ChangeType(text.ToString(), type, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
            {
                if (context.Options.Strict)
                {
                    throw new VslException($"'{text.ToString()}' is not a valid {type.Name} key.", ex);
                }

                key = default;
                return false;
            }
        }
    }

    public sealed class ValueTupleFormatter<T1, T2> : VslFormatter<ValueTuple<T1, T2>>
    {
        private IVslFormatter<T1> _f1;
        private IVslFormatter<T2> _f2;

        public override bool IsScalar =>
            (_f1 ??= VslFormatterRegistry.Get<T1>()).IsScalar &&
            (_f2 ??= VslFormatterRegistry.Get<T2>()).IsScalar;

        public override void Write(ref VslWriter writer, in ValueTuple<T1, T2> value, VslContext context)
        {
            _f1 ??= VslFormatterRegistry.Get<T1>();
            _f2 ??= VslFormatterRegistry.Get<T2>();

            writer.BeginTuple();
            _f1.Write(ref writer, value.Item1, context);
            _f2.Write(ref writer, value.Item2, context);
            writer.EndTuple();
        }

        public override ValueTuple<T1, T2> Read(ref VslReader reader, VslContext context)
        {
            _f1 ??= VslFormatterRegistry.Get<T1>();
            _f2 ??= VslFormatterRegistry.Get<T2>();

            reader.ReadTupleStart();
            var a = reader.AtEnd() ? default : _f1.Read(ref reader, context);
            var b = reader.AtEnd() ? default : _f2.Read(ref reader, context);
            reader.ReadTupleEnd();
            return new ValueTuple<T1, T2>(a, b);
        }
    }

    public sealed class ValueTupleFormatter<T1, T2, T3> : VslFormatter<ValueTuple<T1, T2, T3>>
    {
        private IVslFormatter<T1> _f1;
        private IVslFormatter<T2> _f2;
        private IVslFormatter<T3> _f3;

        public override bool IsScalar =>
            (_f1 ??= VslFormatterRegistry.Get<T1>()).IsScalar &&
            (_f2 ??= VslFormatterRegistry.Get<T2>()).IsScalar &&
            (_f3 ??= VslFormatterRegistry.Get<T3>()).IsScalar;

        public override void Write(ref VslWriter writer, in ValueTuple<T1, T2, T3> value, VslContext context)
        {
            _f1 ??= VslFormatterRegistry.Get<T1>();
            _f2 ??= VslFormatterRegistry.Get<T2>();
            _f3 ??= VslFormatterRegistry.Get<T3>();

            writer.BeginTuple();
            _f1.Write(ref writer, value.Item1, context);
            _f2.Write(ref writer, value.Item2, context);
            _f3.Write(ref writer, value.Item3, context);
            writer.EndTuple();
        }

        public override ValueTuple<T1, T2, T3> Read(ref VslReader reader, VslContext context)
        {
            _f1 ??= VslFormatterRegistry.Get<T1>();
            _f2 ??= VslFormatterRegistry.Get<T2>();
            _f3 ??= VslFormatterRegistry.Get<T3>();

            reader.ReadTupleStart();
            var a = reader.AtEnd() ? default : _f1.Read(ref reader, context);
            var b = reader.AtEnd() ? default : _f2.Read(ref reader, context);
            var c = reader.AtEnd() ? default : _f3.Read(ref reader, context);
            reader.ReadTupleEnd();
            return new ValueTuple<T1, T2, T3>(a, b, c);
        }
    }
}
