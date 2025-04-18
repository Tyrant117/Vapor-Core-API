using System.Collections.Generic;
using Newtonsoft.Json;

namespace Vapor.NewtonsoftConverters
{
    public static class NewtonsoftUtility
    {
        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new FieldsOnlyContractResolver(),
            Converters = new List<JsonConverter>
            {
                new Vector2Converter(), new Vector2IntConverter(), new Vector3Converter(), new Vector3IntConverter(), new Vector4Converter(),
                new ColorConverter(), new RectConverter(), new RectIntConverter(), new BoundsConverter(), new BoundsIntConverter(),
                new LayerMaskConverter(), new RenderingLayerMaskConverter(),
                new AnimationCurveConverter(), new KeyframeConverter(),
                new GradientConverter(), new GradientColorKeyConverter(), new GradientAlphaKeyConverter(),
                new Hash128Converter(), new SerializedObjectConverter(),
            },
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
            // Error = (sender, args) => { args.ErrorContext.Handled = true; }
        };

        public static readonly JsonSerializer JsonSerializer = JsonSerializer.Create(SerializerSettings);
    }
}
