using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Newtonsoft.Json;

namespace Vapor.NewtonsoftConverters
{
    public static class JsonReaderExtensions
    {
        [return: MaybeNull]
        public static T ReadViaSerializer<T>(this JsonReader reader, JsonSerializer serializer)
        {
            reader.Read();
            return serializer.Deserialize<T>(reader);
        }
        
        public static float? ReadAsFloat(this JsonReader reader)
        {
            var str = reader.ReadAsString();
            if (str.EmptyOrNull())
            {
                return null;
            }

            return float.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var valueParsed) ? valueParsed : 0f;
        }
        
        public static byte? ReadAsInt8(this JsonReader reader)
        {
            return checked((byte)(reader.ReadAsInt32() ?? 0));
        }
        
        public static TEnum ReadEnum<TEnum>(this JsonReader reader) where TEnum : struct
        {
            var str = reader.ReadAsString();
            if (!str.EmptyOrNull())
            {
                return Enum.TryParse(str, out TEnum result) ? result : default(TEnum);
            }

            var val = reader.ReadAsInt32() ?? 0;
            return (TEnum)Enum.ToObject(typeof(TEnum), val);

        }
    }
}
