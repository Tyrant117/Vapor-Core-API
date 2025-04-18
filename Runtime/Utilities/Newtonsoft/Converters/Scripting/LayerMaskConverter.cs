using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Vapor.NewtonsoftConverters
{
    public class LayerMaskConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(LayerMask) || objectType == typeof(LayerMask?);
        }

        [return: MaybeNull]
        public override object ReadJson(JsonReader reader, Type objectType, [AllowNull] object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return objectType == typeof(LayerMask?)
                    ? (LayerMask?)null
                    : new LayerMask();
            }

            if (reader.TokenType == JsonToken.Integer)
            {
                return new LayerMask {
                    value = GetInt(reader.Value)
                };
            }

            throw new JsonReaderException($"Unexpected token when reading LayerMask. Expected 'null' or 'integer', got '{reader.TokenType}'.");
        }

        private static int GetInt(object value)
        {
            switch (value)
            {
                case int i: return i;
                case uint ui: return checked((int)ui);
                case long l: return checked((int)l);
                case ulong ul: return checked((int)ul);
                default: return 0;
            }
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] object value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
            }
            else
            {
                var layerMask = (LayerMask)value;
                writer.WriteValue(layerMask.value);
            }
        }
    }
    
    public class RenderingLayerMaskConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(RenderingLayerMask) || objectType == typeof(RenderingLayerMask?);
        }

        [return: MaybeNull]
        public override object ReadJson(JsonReader reader, Type objectType, [AllowNull] object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return objectType == typeof(RenderingLayerMask?)
                    ? (RenderingLayerMask?)null
                    : new RenderingLayerMask();
            }

            if (reader.TokenType == JsonToken.Integer)
            {
                return new RenderingLayerMask {
                    value = GetInt(reader.Value)
                };
            }

            throw new JsonReaderException($"Unexpected token when reading RenderingLayerMask. Expected 'null' or 'integer', got '{reader.TokenType}'.");
        }

        private static uint GetInt(object value)
        {
            return value switch
            {
                int i => (uint)i,
                uint ui => ui,
                long l => checked((uint)l),
                ulong ul => checked((uint)ul),
                _ => 0
            };
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] object value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
            }
            else
            {
                var layerMask = (RenderingLayerMask)value;
                writer.WriteValue(layerMask.value);
            }
        }
    }
    
    public class Hash128Converter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Hash128) || objectType == typeof(Hash128?);
        }

        [return: MaybeNull]
        public override object ReadJson(JsonReader reader, Type objectType, [AllowNull] object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return objectType == typeof(Hash128?)
                    ? (Hash128?)null
                    : new Hash128();
            }

            if (reader is { TokenType: JsonToken.String, Value: string stringValue })
            {
                return Hash128.Parse(stringValue);
            }

            throw new JsonReaderException($"Expected string when reading UnityEngine.Hash128 type, got '{reader.TokenType}' <{reader.Value}>.");
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] object value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteValue(((Hash128)value).ToString());
            }
        }
    }

    public class SerializedObjectConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(GameObject).IsAssignableFrom(objectType) ||
                   typeof(ScriptableObject).IsAssignableFrom(objectType);
        }

        [return: MaybeNull]
        public override object ReadJson(JsonReader reader, Type objectType, [AllowNull] object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }
            
#if UNITY_EDITOR
            if (reader.TokenType == JsonToken.StartObject)
            {
                JObject obj = JObject.Load(reader);

                if (!(obj.TryGetValue("guid", out JToken guidToken) && obj.TryGetValue("type", out JToken typeToken)))
                {
                    return obj.ToObject(objectType, serializer); // Handle non-Unity objects normally
                }

                string guid = guidToken.ToString();
                string typeName = typeToken.ToString();
                Type targetType = Type.GetType(typeName);

                if (targetType == null || !typeof(Object).IsAssignableFrom(targetType))
                {
                    throw new JsonReaderException($"Unknown or unsupported type: {typeName}");
                }

                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                return UnityEditor.AssetDatabase.LoadAssetAtPath(path, targetType);
            }
#endif
            return serializer.Deserialize(reader, objectType);
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] object value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
            }
            else
            {
                #if UNITY_EDITOR
                if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier((Object)value, out var guid, out var localFile))
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("type");
                    writer.WriteValue(value.GetType().AssemblyQualifiedName); // Store type info
                    writer.WritePropertyName("guid");
                    writer.WriteValue(guid);
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteNull();
                }
                #else
                    writer.WriteNull();
                #endif
            }
        }
    }
    
    
}
