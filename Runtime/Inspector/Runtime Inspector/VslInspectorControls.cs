using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    /// <summary>
    /// The controls a runtime inspector draws one value with, chosen by type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately narrow: what a build's debug window needs is the types a template actually holds —
    /// numbers, flags, vectors, a pose — and a legible read-only fallback for everything else. There is
    /// no drawer registry and no attribute handling here; that is the editor inspector's job, and it is
    /// not available in a player.
    /// </para>
    /// <para>
    /// Two things are not the type they look like. A flags enum is a <see cref="MaskField"/> because
    /// UI Toolkit's flags control is editor-only, and the mapping from enum bits to mask positions is
    /// done here. A quaternion is drawn as euler angles, because nobody debugs a rotation in xyzw.
    /// </para>
    /// </remarks>
    internal static class VslInspectorControls
    {
        /// <summary>
        /// Builds a control for <paramref name="type"/>, or returns null when this type has no editor
        /// here and the caller should nest into it or fall back to text.
        /// </summary>
        /// <param name="setValue">Pushes a value into the control without raising a change.</param>
        public static VisualElement Create(Type type, object initial, Action<object> onChanged, out Action<object> setValue)
        {
            setValue = null;
            if (type == null)
            {
                return null;
            }

            if (type == typeof(bool))
            {
                var toggle = new Toggle { value = initial is true };
                toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => toggle.SetValueWithoutNotify(v is true);
                return toggle;
            }

            if (type == typeof(string))
            {
                var text = new TextField { value = initial as string ?? string.Empty, isDelayed = true };
                text.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => text.SetValueWithoutNotify(v as string ?? string.Empty);
                return text;
            }

            if (type.IsEnum)
            {
                return CreateEnum(type, initial, onChanged, out setValue);
            }

            if (IsSignedIntegral(type))
            {
                // One control for every width: the value is converted back to the member's own type on
                // the way out, so a byte cannot be handed 300 and a short cannot wrap silently.
                var field = new LongField { value = ToLong(initial), isDelayed = true };
                field.RegisterValueChangedCallback(evt =>
                {
                    if (TryConvertIntegral(type, evt.newValue, out object converted))
                    {
                        onChanged(converted);
                    }
                    else
                    {
                        field.SetValueWithoutNotify(ToLong(initial));
                    }
                });
                setValue = v => field.SetValueWithoutNotify(ToLong(v));
                return field;
            }

            if (IsUnsignedIntegral(type))
            {
                var field = new UnsignedLongField { value = ToULong(initial), isDelayed = true };
                field.RegisterValueChangedCallback(evt =>
                {
                    if (TryConvertUnsigned(type, evt.newValue, out object converted))
                    {
                        onChanged(converted);
                    }
                    else
                    {
                        field.SetValueWithoutNotify(ToULong(initial));
                    }
                });
                setValue = v => field.SetValueWithoutNotify(ToULong(v));
                return field;
            }

            if (type == typeof(float))
            {
                var field = new FloatField { value = initial is float f ? f : 0f, isDelayed = true };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is float value ? value : 0f);
                return field;
            }

            if (type == typeof(double))
            {
                var field = new DoubleField { value = initial is double d ? d : 0d, isDelayed = true };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is double value ? value : 0d);
                return field;
            }

            if (type == typeof(Vector2))
            {
                var field = new Vector2Field { value = initial is Vector2 v2 ? v2 : Vector2.zero };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is Vector2 value ? value : Vector2.zero);
                return field;
            }

            if (type == typeof(Vector3))
            {
                var field = new Vector3Field { value = initial is Vector3 v3 ? v3 : Vector3.zero };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is Vector3 value ? value : Vector3.zero);
                return field;
            }

            if (type == typeof(Vector4))
            {
                var field = new Vector4Field { value = initial is Vector4 v4 ? v4 : Vector4.zero };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is Vector4 value ? value : Vector4.zero);
                return field;
            }

            if (type == typeof(Vector2Int))
            {
                var field = new Vector2IntField { value = initial is Vector2Int v ? v : Vector2Int.zero };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is Vector2Int value ? value : Vector2Int.zero);
                return field;
            }

            if (type == typeof(Vector3Int))
            {
                var field = new Vector3IntField { value = initial is Vector3Int v ? v : Vector3Int.zero };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is Vector3Int value ? value : Vector3Int.zero);
                return field;
            }

            if (type == typeof(Quaternion))
            {
                return CreateQuaternion(initial, onChanged, out setValue);
            }

            if (type == typeof(Color))
            {
                return CreateColor(initial, onChanged, out setValue);
            }

            if (type == typeof(Rect))
            {
                var field = new RectField { value = initial is Rect r ? r : default };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is Rect value ? value : default);
                return field;
            }

            if (type == typeof(Bounds))
            {
                var field = new BoundsField { value = initial is Bounds b ? b : default };
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v => field.SetValueWithoutNotify(v is Bounds value ? value : default);
                return field;
            }

            return null;
        }

        #region - Enums -

        private static VisualElement CreateEnum(Type type, object initial, Action<object> onChanged, out Action<object> setValue)
        {
            var current = initial as Enum ?? (Enum)Enum.ToObject(type, 0);

            if (!type.IsDefined(typeof(FlagsAttribute), false))
            {
                var field = new EnumField();
                field.Init(current);
                field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
                setValue = v =>
                {
                    if (v is Enum value)
                    {
                        field.SetValueWithoutNotify(value);
                    }
                };

                return field;
            }

            // UI Toolkit's flags control is editor-only, so the bits are mapped onto a plain mask:
            // choice i stands for the i'th single-bit value the enum declares, in declared order.
            var bits = new List<long>();
            var names = new List<string>();
            foreach (var value in Enum.GetValues(type))
            {
                long bit = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                if (bit != 0 && (bit & (bit - 1)) == 0 && !bits.Contains(bit))
                {
                    bits.Add(bit);
                    names.Add(Enum.GetName(type, value));
                }
            }

            var mask = new MaskField(names, ToMask(current, bits));
            mask.RegisterValueChangedCallback(evt => onChanged(Enum.ToObject(type, FromMask(evt.newValue, bits))));
            setValue = v =>
            {
                if (v is Enum value)
                {
                    mask.SetValueWithoutNotify(ToMask(value, bits));
                }
            };

            return mask;
        }

        private static int ToMask(Enum value, List<long> bits)
        {
            long raw = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            int mask = 0;
            for (int i = 0; i < bits.Count; i++)
            {
                if ((raw & bits[i]) == bits[i])
                {
                    mask |= 1 << i;
                }
            }

            return mask;
        }

        private static long FromMask(int mask, List<long> bits)
        {
            long raw = 0;
            for (int i = 0; i < bits.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    raw |= bits[i];
                }
            }

            return raw;
        }

        #endregion

        #region - Composed -

        /// <summary>
        /// A rotation as euler angles. Round-tripping through euler is lossy in the sense that the
        /// numbers shown are not the only ones that produce the same rotation, but it is the only form
        /// anyone can read or type, and a debug window is for reading and typing.
        /// </summary>
        private static VisualElement CreateQuaternion(object initial, Action<object> onChanged, out Action<object> setValue)
        {
            var current = initial is Quaternion q ? q : Quaternion.identity;
            var field = new Vector3Field { value = current.eulerAngles };

            field.RegisterValueChangedCallback(evt => onChanged(Quaternion.Euler(evt.newValue)));
            setValue = v =>
            {
                if (v is Quaternion value)
                {
                    field.SetValueWithoutNotify(value.eulerAngles);
                }
            };

            return field;
        }

        /// <summary>A colour as its hex string, with a swatch. UI Toolkit's colour picker is editor-only.</summary>
        private static VisualElement CreateColor(object initial, Action<object> onChanged, out Action<object> setValue)
        {
            var current = initial is Color c ? c : Color.white;

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1f } };
            var swatch = new VisualElement
            {
                style =
                {
                    width = 16, height = 16, marginRight = 4, flexShrink = 0f,
                    backgroundColor = current,
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = Color.black, borderBottomColor = Color.black,
                    borderLeftColor = Color.black, borderRightColor = Color.black,
                },
            };

            var text = new TextField { value = "#" + ColorUtility.ToHtmlStringRGBA(current), isDelayed = true, style = { flexGrow = 1f } };
            text.RegisterValueChangedCallback(evt =>
            {
                string typed = evt.newValue.StartsWith("#", StringComparison.Ordinal) ? evt.newValue : "#" + evt.newValue;
                if (ColorUtility.TryParseHtmlString(typed, out var parsed))
                {
                    swatch.style.backgroundColor = parsed;
                    onChanged(parsed);
                }
                else
                {
                    text.SetValueWithoutNotify("#" + ColorUtility.ToHtmlStringRGBA(current));
                }
            });

            setValue = v =>
            {
                if (v is not Color value)
                {
                    return;
                }

                swatch.style.backgroundColor = value;
                text.SetValueWithoutNotify("#" + ColorUtility.ToHtmlStringRGBA(value));
            };

            row.Add(swatch);
            row.Add(text);
            return row;
        }

        #endregion

        #region - Numbers -

        public static bool IsSignedIntegral(Type type) =>
            type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(sbyte);

        public static bool IsUnsignedIntegral(Type type) =>
            type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(byte);

        private static long ToLong(object value) =>
            value == null ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);

        private static ulong ToULong(object value) =>
            value == null ? 0UL : Convert.ToUInt64(value, CultureInfo.InvariantCulture);

        private static bool TryConvertIntegral(Type type, long value, out object converted)
        {
            converted = null;
            try
            {
                converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryConvertUnsigned(Type type, ulong value, out object converted)
        {
            converted = null;
            try
            {
                converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        #endregion
    }
}
