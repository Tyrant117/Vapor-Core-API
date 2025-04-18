using Newtonsoft.Json;
using UnityEngine;
using System;

namespace Vapor.NewtonsoftConverters
{
    public class RectConverter : BaseConverter<Rect>
    {
        protected override void ReadValue(ref Rect value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.x):
                    value.x = reader.ReadAsFloat() ?? 0;
                    break;
                case nameof(value.y):
                    value.y = reader.ReadAsFloat() ?? 0;
                    break;
                case nameof(value.width):
                    value.width = reader.ReadAsFloat() ?? 0;
                    break;
                case nameof(value.height):
                    value.height = reader.ReadAsFloat() ?? 0;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Rect value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.x));
            writer.WriteValue(value.x);
            writer.WritePropertyName(nameof(value.y));
            writer.WriteValue(value.y);
            writer.WritePropertyName(nameof(value.width));
            writer.WriteValue(value.width);
            writer.WritePropertyName(nameof(value.height));
            writer.WriteValue(value.height);
        }
    }
    
    public class RectIntConverter : BaseConverter<RectInt>
    {
        protected override void ReadValue(ref RectInt value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.x):
                    value.x = reader.ReadAsInt32() ?? 0;
                    break;
                case nameof(value.y):
                    value.y = reader.ReadAsInt32() ?? 0;
                    break;
                case nameof(value.width):
                    value.width = reader.ReadAsInt32() ?? 0;
                    break;
                case nameof(value.height):
                    value.height = reader.ReadAsInt32() ?? 0;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, RectInt value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.x));
            writer.WriteValue(value.x);
            writer.WritePropertyName(nameof(value.y));
            writer.WriteValue(value.y);
            writer.WritePropertyName(nameof(value.width));
            writer.WriteValue(value.width);
            writer.WritePropertyName(nameof(value.height));
            writer.WriteValue(value.height);
        }
    }
    
    public class BoundsConverter : BaseConverter<Bounds>
    {
        protected override void ReadValue(ref Bounds value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.center):
                    value.center = reader.ReadViaSerializer<Vector3>(serializer);
                    break;
                case nameof(value.size):
                    value.size = reader.ReadViaSerializer<Vector3>(serializer);
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Bounds value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.center));
            serializer.Serialize(writer, value.center, typeof(Vector3));
            writer.WritePropertyName(nameof(value.size));
            serializer.Serialize(writer, value.size, typeof(Vector3));
        }
    }
    
    public class BoundsIntConverter : BaseConverter<BoundsInt>
    {
        protected override void ReadValue(ref BoundsInt value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.position):
                    value.position = reader.ReadViaSerializer<Vector3Int>(serializer);
                    break;
                case nameof(value.size):
                    value.size = reader.ReadViaSerializer<Vector3Int>(serializer);
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, BoundsInt value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.position));
            serializer.Serialize(writer, value.position, typeof(Vector3Int));
            writer.WritePropertyName(nameof(value.size));
            serializer.Serialize(writer, value.size, typeof(Vector3Int));
        }
    }
}
