using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    // Drawers for the Burst maths types.
    //
    // The tree expands an unrecognised struct into its public fields, which for these means x, y and z
    // on separate rows — correct, and nothing like how a vector is meant to be read or edited. Unity's
    // own inspector has drawers for them; the tree does not inherit those, because it draws plain C#
    // objects that have no SerializedProperty behind them.
    //
    // Only CreateVaporPropertyGUI is overridden, so serialized inspectors keep whatever they had.

    /// <summary>Draws a <see cref="float2"/> as one vector control.</summary>
    [CustomPropertyDrawer(typeof(float2), true)]
    public sealed class Float2Drawer : VaporPropertyDrawer
    {
        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            var value = field.Property.GetValue<float2>();

            var control = new Vector2Field(ObjectNames.NicifyVariableName(field.Property.PropertyName))
            {
                value = new Vector2(value.x, value.y),
            };

            control.RegisterValueChangedCallback(e =>
                field.Property.SetValue(new float2(e.newValue.x, e.newValue.y)));

            return control;
        }
    }

    /// <summary>Draws a <see cref="float3"/> as one vector control.</summary>
    [CustomPropertyDrawer(typeof(float3), true)]
    public sealed class Float3Drawer : VaporPropertyDrawer
    {
        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            var value = field.Property.GetValue<float3>();

            var control = new Vector3Field(ObjectNames.NicifyVariableName(field.Property.PropertyName))
            {
                value = new Vector3(value.x, value.y, value.z),
            };

            control.RegisterValueChangedCallback(e =>
                field.Property.SetValue(new float3(e.newValue.x, e.newValue.y, e.newValue.z)));

            return control;
        }
    }

    /// <summary>
    /// Draws a <see cref="float4"/> as an HDR colour when it is named like one, and as four numbers
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// Guessing from the field's name is crude and is the only signal there is. A float4 is how an HDR
    /// colour is carried in Burst code, where <see cref="Color"/> cannot go — and it is equally how a
    /// plane, a set of band levels or a pair of ranges is carried. The type says nothing about which,
    /// so the name has to.
    /// <para/>
    /// The picker is HDR because these colours genuinely exceed one; that is usually the reason the
    /// value is a float4 rather than a <see cref="Color32"/>. A clamped picker would quietly destroy
    /// that range on the first edit.
    /// </remarks>
    [CustomPropertyDrawer(typeof(float4), true)]
    public sealed class Float4Drawer : VaporPropertyDrawer
    {
        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            var value = field.Property.GetValue<float4>();
            var name = field.Property.PropertyName ?? string.Empty;
            var label = ObjectNames.NicifyVariableName(name);

            var looksLikeColour = name.Contains("Color", StringComparison.OrdinalIgnoreCase)
                                  || name.Contains("Colour", StringComparison.OrdinalIgnoreCase)
                                  || name.Contains("Tint", StringComparison.OrdinalIgnoreCase);

            if (looksLikeColour)
            {
                var colour = new ColorField(label)
                {
                    value = new Color(value.x, value.y, value.z, value.w),
                    hdr = true,
                    showAlpha = true,
                };

                colour.RegisterValueChangedCallback(e =>
                    field.Property.SetValue(new float4(e.newValue.r, e.newValue.g, e.newValue.b, e.newValue.a)));

                return colour;
            }

            var vector = new Vector4Field(label)
            {
                value = new Vector4(value.x, value.y, value.z, value.w),
            };

            vector.RegisterValueChangedCallback(e =>
                field.Property.SetValue(new float4(e.newValue.x, e.newValue.y, e.newValue.z, e.newValue.w)));

            return vector;
        }
    }

    /// <summary>Draws an <see cref="int2"/> as one vector control.</summary>
    [CustomPropertyDrawer(typeof(int2), true)]
    public sealed class Int2Drawer : VaporPropertyDrawer
    {
        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            var value = field.Property.GetValue<int2>();

            var control = new Vector2IntField(ObjectNames.NicifyVariableName(field.Property.PropertyName))
            {
                value = new Vector2Int(value.x, value.y),
            };

            control.RegisterValueChangedCallback(e =>
                field.Property.SetValue(new int2(e.newValue.x, e.newValue.y)));

            return control;
        }
    }

    /// <summary>Draws an <see cref="int3"/> as one vector control.</summary>
    [CustomPropertyDrawer(typeof(int3), true)]
    public sealed class Int3Drawer : VaporPropertyDrawer
    {
        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            var value = field.Property.GetValue<int3>();

            var control = new Vector3IntField(ObjectNames.NicifyVariableName(field.Property.PropertyName))
            {
                value = new Vector3Int(value.x, value.y, value.z),
            };

            control.RegisterValueChangedCallback(e =>
                field.Property.SetValue(new int3(e.newValue.x, e.newValue.y, e.newValue.z)));

            return control;
        }
    }
}
