using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Vapor.Inspector
{
    public static class EnumDataUtility
    {
        public enum CachedType
        {
            ExcludeObsolete,
            IncludeObsoleteExceptErrors,
            IncludeAllObsolete
        }

        private static PropertyInfo _inspectorSortProperty;
        private static PropertyInfo _sortDirectionProperty;

        private static readonly Dictionary<(CachedType, Type), EnumData> s_EnumData = new Dictionary<(CachedType, Type), EnumData>();

        public static EnumData GetCachedEnumData(Type enumType, CachedType cachedType = CachedType.IncludeObsoleteExceptErrors, Func<string, string> nicifyName = null)
        {
            if (s_EnumData.TryGetValue((cachedType, enumType), out var value))
            {
                return value;
            }

            EnumData enumData = default(EnumData);
            enumData.underlyingType = Enum.GetUnderlyingType(enumType);
            value = enumData;
            value.unsigned = value.underlyingType == typeof(byte) || value.underlyingType == typeof(ushort) || value.underlyingType == typeof(uint) || value.underlyingType == typeof(ulong);
            FieldInfo[] fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
            List<FieldInfo> list = new List<FieldInfo>();
            int num = fields.Length;
            for (int i = 0; i < num; i++)
            {
                if (CheckObsoleteAddition(fields[i], cachedType))
                {
                    list.Add(fields[i]);
                }
            }

            if (!list.Any())
            {
                string[] array = new string[1] { "" };
                Enum[] values = new Enum[0];
                int[] flagValues = new int[1];
                value.values = values;
                value.flagValues = flagValues;
                value.displayNames = array;
                value.names = array;
                value.tooltip = array;
                value.flags = true;
                value.serializable = true;
                return value;
            }

            try
            {
                string location = list.First().Module.Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    list = list.OrderBy((FieldInfo f) => f.MetadataToken).ToList();
                }
            }
            catch
            {
            }

            value.displayNames = list.Select((FieldInfo f) => EnumNameFromEnumField(f, nicifyName)).ToArray();
            if (value.displayNames.Distinct().Count() != value.displayNames.Length)
            {
                Debug.LogWarning("Enum " + enumType.Name + " has multiple entries with the same display name, this prevents selection in EnumPopup.");
            }

            value.tooltip = list.Select((FieldInfo f) => EnumTooltipFromEnumField(f)).ToArray();
            value.values = list.Select((FieldInfo f) => (Enum)f.GetValue(null)).ToArray();
            value.flagValues = (value.unsigned ? value.values.Select((Enum v) => (int)Convert.ToUInt64(v)).ToArray() : value.values.Select((Enum v) => (int)Convert.ToInt64(v)).ToArray());
            value.names = new string[value.values.Length];
            for (int j = 0; j < list.Count; j++)
            {
                value.names[j] = list[j].Name;
            }

            if (value.underlyingType == typeof(ushort))
            {
                int k = 0;
                for (int num2 = value.flagValues.Length; k < num2; k++)
                {
                    if ((long)value.flagValues[k] == 65535)
                    {
                        value.flagValues[k] = -1;
                    }
                }
            }
            else if (value.underlyingType == typeof(byte))
            {
                int l = 0;
                for (int num3 = value.flagValues.Length; l < num3; l++)
                {
                    if ((long)value.flagValues[l] == 255)
                    {
                        value.flagValues[l] = -1;
                    }
                }
            }

            value.flags = enumType.IsDefined(typeof(FlagsAttribute), inherit: false);
            value.serializable = value.underlyingType != typeof(long) && value.underlyingType != typeof(ulong);
            HandleInspectorOrderAttribute(enumType, ref value);
            s_EnumData[(cachedType, enumType)] = value;
            return value;
        }

        public static int EnumFlagsToInt(EnumData enumData, Enum enumValue)
        {
            if (enumData.unsigned)
            {
                if (enumData.underlyingType == typeof(uint))
                {
                    return (int)Convert.ToUInt32(enumValue);
                }

                if (enumData.underlyingType == typeof(ushort))
                {
                    ushort num = Convert.ToUInt16(enumValue);
                    return (num == ushort.MaxValue) ? (-1) : num;
                }

                byte b = Convert.ToByte(enumValue);
                return (b == byte.MaxValue) ? (-1) : b;
            }

            return Convert.ToInt32(enumValue);
        }

        public static Enum IntToEnumFlags(Type enumType, int value)
        {
            EnumData cachedEnumData = GetCachedEnumData(enumType);
            if (cachedEnumData.unsigned)
            {
                if (cachedEnumData.underlyingType == typeof(uint))
                {
                    uint num = (uint)value;
                    return Enum.Parse(enumType, num.ToString()) as Enum;
                }

                if (cachedEnumData.underlyingType == typeof(ushort))
                {
                    return Enum.Parse(enumType, ((ushort)value).ToString()) as Enum;
                }

                return Enum.Parse(enumType, ((byte)value).ToString()) as Enum;
            }

            return Enum.Parse(enumType, value.ToString()) as Enum;
        }

        public static void HandleInspectorOrderAttribute(Type enumType, ref EnumData enumData)
        {
            if (Attribute.GetCustomAttribute(enumType, typeof(InspectorOrderAttribute)) is InspectorOrderAttribute inspectorOrderAttribute)
            {
                int num = enumData.displayNames.Length;
                int[] array = new int[num];
                for (int i = 0; i < num; i++)
                {
                    array[i] = i;
                }

                _inspectorSortProperty ??= typeof(InspectorOrderAttribute).GetProperty("m_inspectorSort", BindingFlags.NonPublic | BindingFlags.Instance);
                InspectorSort inspectorSort = (InspectorSort)_inspectorSortProperty.GetValue(inspectorOrderAttribute);
                InspectorSort inspectorSort2 = inspectorSort;
                if (inspectorSort2 == InspectorSort.ByValue)
                {
                    int[] array2 = new int[num];
                    Array.Copy(enumData.flagValues, array2, num);
                    Array.Sort(array2, array);
                }
                else
                {
                    string[] array3 = new string[num];
                    Array.Copy(enumData.displayNames, array3, num);
                    Array.Sort(array3, array, StringComparer.Ordinal);
                }

                _sortDirectionProperty ??= typeof(InspectorOrderAttribute).GetProperty("m_sortDirection", BindingFlags.NonPublic | BindingFlags.Instance);
                if ((InspectorSortDirection)_sortDirectionProperty.GetValue(inspectorOrderAttribute) == InspectorSortDirection.Descending)
                {
                    Array.Reverse(array);
                }

                Enum[] array4 = new Enum[num];
                int[] array5 = new int[num];
                string[] array6 = new string[num];
                string[] array7 = new string[num];
                string[] array8 = new string[num];
                for (int j = 0; j < num; j++)
                {
                    int num2 = array[j];
                    array4[j] = enumData.values[num2];
                    array5[j] = enumData.flagValues[num2];
                    array6[j] = enumData.displayNames[num2];
                    array7[j] = enumData.names[num2];
                    array8[j] = enumData.tooltip[num2];
                }

                enumData.values = array4;
                enumData.flagValues = array5;
                enumData.displayNames = array6;
                enumData.names = array7;
                enumData.tooltip = array8;
            }
        }

        private static bool CheckObsoleteAddition(FieldInfo field, CachedType cachedType)
        {
            object[] customAttributes = field.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false);
            if (customAttributes.Length != 0)
            {
                return cachedType switch
                {
                    CachedType.ExcludeObsolete => false,
                    CachedType.IncludeAllObsolete => true,
                    _ => !((ObsoleteAttribute)customAttributes.First()).IsError,
                };
            }

            return true;
        }

        private static string EnumTooltipFromEnumField(FieldInfo field)
        {
            object[] customAttributes = field.GetCustomAttributes(typeof(TooltipAttribute), inherit: false);
            if (customAttributes.Length != 0)
            {
                return ((TooltipAttribute)customAttributes.First()).tooltip;
            }

            return string.Empty;
        }

        private static string EnumNameFromEnumField(FieldInfo field, Func<string, string> nicifyName)
        {
            object[] customAttributes = field.GetCustomAttributes(typeof(InspectorNameAttribute), inherit: false);
            if (customAttributes.Length != 0)
            {
                return ((InspectorNameAttribute)customAttributes.First()).displayName;
            }

            if (field.IsDefined(typeof(ObsoleteAttribute), inherit: false))
            {
                return NicifyName() + " (Obsolete)";
            }

            return NicifyName();
            string NicifyName()
            {
                return (nicifyName == null) ? field.Name : nicifyName(field.Name);
            }
        }
    }
}
