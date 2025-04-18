using Newtonsoft.Json;
using UnityEngine;

namespace Vapor.NewtonsoftConverters
{
    public class Vector2Converter : BaseConverter<Vector2>
    {
        protected override void ReadValue(ref Vector2 value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.x):
                    value.x = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.y):
                    value.y = reader.ReadAsFloat() ?? 0f;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.x));
            writer.WriteValue(value.x);
            writer.WritePropertyName(nameof(value.y));
            writer.WriteValue(value.y);
        }
    }
    
    public class Vector2IntConverter : BaseConverter<Vector2Int>
    {
        protected override void ReadValue(ref Vector2Int value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.x):
                    value.x = reader.ReadAsInt32() ?? 0;
                    break;
                case nameof(value.y):
                    value.y = reader.ReadAsInt32() ?? 0;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Vector2Int value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.x));
            writer.WriteValue(value.x);
            writer.WritePropertyName(nameof(value.y));
            writer.WriteValue(value.y);
        }
    }
    
    public class Vector3Converter : BaseConverter<Vector3>
    {
        protected override void ReadValue(ref Vector3 value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.x):
                    value.x = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.y):
                    value.y = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.z):
                    value.z = reader.ReadAsFloat() ?? 0f;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.x));
            writer.WriteValue(value.x);
            writer.WritePropertyName(nameof(value.y));
            writer.WriteValue(value.y);
            writer.WritePropertyName(nameof(value.z));
            writer.WriteValue(value.z);
        }
    }
    
    public class Vector3IntConverter : BaseConverter<Vector3Int>
    {
        protected override void ReadValue(ref Vector3Int value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.x):
                    value.x = reader.ReadAsInt32() ?? 0;
                    break;
                case nameof(value.y):
                    value.y = reader.ReadAsInt32() ?? 0;
                    break;
                case nameof(value.z):
                    value.z = reader.ReadAsInt32() ?? 0;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Vector3Int value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.x));
            writer.WriteValue(value.x);
            writer.WritePropertyName(nameof(value.y));
            writer.WriteValue(value.y);
            writer.WritePropertyName(nameof(value.z));
            writer.WriteValue(value.z);
        }
    }
    
    public class Vector4Converter : BaseConverter<Vector4>
    {
        protected override void ReadValue(ref Vector4 value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.x):
                    value.x = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.y):
                    value.y = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.z):
                    value.z = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.w):
                    value.w = reader.ReadAsFloat() ?? 0f;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Vector4 value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.x));
            writer.WriteValue(value.x);
            writer.WritePropertyName(nameof(value.y));
            writer.WriteValue(value.y);
            writer.WritePropertyName(nameof(value.z));
            writer.WriteValue(value.z);
            writer.WritePropertyName(nameof(value.w));
            writer.WriteValue(value.w);
        }
    }
    
    public class ColorConverter : BaseConverter<Color>
    {
        protected override void ReadValue(ref Color value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.r):
                    value.r = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.g):
                    value.g = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.b):
                    value.b = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.a):
                    value.a = reader.ReadAsFloat() ?? 0f;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Color value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.r));
            writer.WriteValue(value.r);
            writer.WritePropertyName(nameof(value.g));
            writer.WriteValue(value.g);
            writer.WritePropertyName(nameof(value.b));
            writer.WriteValue(value.b);
            writer.WritePropertyName(nameof(value.a));
            writer.WriteValue(value.a);
        }
    }

    public class GradientConverter : BaseConverter<Gradient>
    {
        protected override void ReadValue(ref Gradient value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.colorKeys):
                    value.colorKeys = reader.ReadViaSerializer<GradientColorKey[]>(serializer);
                    break;
                case nameof(value.alphaKeys):
                    value.alphaKeys = reader.ReadViaSerializer<GradientAlphaKey[]>(serializer);
                    break;
                case nameof(value.mode):
                    value.mode = reader.ReadEnum<GradientMode>();
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Gradient value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.colorKeys));
            serializer.Serialize(writer, value.colorKeys, typeof(GradientColorKey[]));

            writer.WritePropertyName(nameof(value.alphaKeys));
            serializer.Serialize(writer, value.alphaKeys, typeof(GradientAlphaKey[]));

            writer.WritePropertyName(nameof(value.mode));
            writer.WriteValue(value.mode.ToString());
        }
    }

    public class GradientColorKeyConverter : BaseConverter<GradientColorKey>
    {
        protected override void ReadValue(ref GradientColorKey value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.color):
                    value.color = reader.ReadViaSerializer<Color>(serializer);
                    break;
                case nameof(value.time):
                    value.time = reader.ReadAsFloat() ?? 0f;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, GradientColorKey value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.color));
            serializer.Serialize(writer, value.color, typeof(Color));
            writer.WritePropertyName(nameof(value.time));
            writer.WriteValue(value.time);
        }
    }
    
    public class GradientAlphaKeyConverter : BaseConverter<GradientAlphaKey>
    {
        protected override void ReadValue(ref GradientAlphaKey value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.alpha):
                    value.alpha = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.time):
                    value.time = reader.ReadAsFloat() ?? 0f;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, GradientAlphaKey value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.alpha));
            writer.WriteValue(value.alpha);
            writer.WritePropertyName(nameof(value.time));
            writer.WriteValue(value.time);
        }
    }
    
    public class AnimationCurveConverter : BaseConverter<AnimationCurve>
    {
        protected override void ReadValue(ref AnimationCurve value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.keys):
                    value.keys = reader.ReadViaSerializer<Keyframe[]>(serializer);
                    break;
                case nameof(value.preWrapMode):
                    value.preWrapMode = reader.ReadEnum<WrapMode>();
                    break;
                case nameof(value.postWrapMode):
                    value.postWrapMode = reader.ReadEnum<WrapMode>();
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, AnimationCurve value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.keys));
            serializer.Serialize(writer, value.keys, typeof(Keyframe[]));

            writer.WritePropertyName(nameof(value.preWrapMode));
            writer.WriteValue(value.preWrapMode.ToString());

            writer.WritePropertyName(nameof(value.postWrapMode));
            writer.WriteValue(value.postWrapMode.ToString());
        }
    }
    
    public class KeyframeConverter : BaseConverter<Keyframe>
    {
        protected override void ReadValue(ref Keyframe value, string name, JsonReader reader, JsonSerializer serializer)
        {
            switch (name)
            {
                case nameof(value.time):
                    value.time = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.value):
                    value.value = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.inTangent):
                    value.inTangent = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.outTangent):
                    value.outTangent = reader.ReadAsFloat() ?? 0f;
                    break;
                case nameof(value.tangentMode):
                    value.tangentMode = reader.ReadAsInt32() ?? 0;
                    break;
            }
        }

        protected override void WriteJsonProperties(JsonWriter writer, Keyframe value, JsonSerializer serializer)
        {
            writer.WritePropertyName(nameof(value.time));
            writer.WriteValue(value.time);

            writer.WritePropertyName(nameof(value.value));
            writer.WriteValue(value.value);

            writer.WritePropertyName(nameof(value.inTangent));
            writer.WriteValue(value.inTangent);

            writer.WritePropertyName(nameof(value.outTangent));
            writer.WriteValue(value.outTangent);
            
            writer.WritePropertyName(nameof(value.tangentMode));
            writer.WriteValue(value.tangentMode);
        }
    }
}
