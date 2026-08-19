using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Serialization;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// Draws an <see cref="AssetRef{T}"/> as the object field it stands in for: pick the asset, and
    /// the locator VSL will write is worked out and shown underneath.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value stored is only ever the locator. Assigning an asset asks the editor locator for its
    /// durable key — a Resources path or an addressable address, publishing the asset as addressable
    /// when it is neither, since referencing it is taken as intent to ship it — and displaying the
    /// field loads the asset back through the asset database, never through Addressables, so it
    /// works before any content has been built.
    /// </para>
    /// <para>
    /// Both inspector paths are covered: <see cref="CreateVaporPropertyGUI"/> for the property tree
    /// (plain C# objects such as data documents) and <see cref="CreatePropertyGUI"/> for serialized
    /// Unity objects.
    /// </para>
    /// <para>
    /// Right-click the row for <c>Set Null</c>. It is the only way to empty a locator whose asset the
    /// project no longer has: the object field shows nothing in that case, and clearing an already
    /// empty field raises no change, so the stale key would otherwise be stuck there.
    /// </para>
    /// </remarks>
    [CustomPropertyDrawer(typeof(AssetRef<>), true)]
    public sealed class AssetRefDrawer : VaporPropertyDrawer
    {
        // Field names on AssetRef<T>.
        private const string SourcePath = "_source";
        private const string KeyPath = "_key";


        #region Vapor tree

        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            var refType = field.PropertyType;
            var assetType = AssetTypeOf(refType);
            if (assetType == null)
            {
                return null;
            }

            var (source, key) = Read(field.Property.GetValue());
            var tooltip = field.Property.TryGetAttribute<VslCommentAttribute>(out var comment) ? comment.Comment : null;

            return BuildRow(field.Property.DisplayName, tooltip, assetType, source, key, (nextSource, nextKey) =>
            {
                var previous = field.Property.GetValue();
                var next = Activator.CreateInstance(refType, nextSource, nextKey);
                field.MarkDirtyWithValue(previous, next);
            });
        }

        #endregion

        #region Serialized objects

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var sourceProperty = property.FindPropertyRelative(SourcePath);
            var keyProperty = property.FindPropertyRelative(KeyPath);
            var assetType = AssetTypeOf(ElementType(fieldInfo?.FieldType));

            if (sourceProperty == null || keyProperty == null || assetType == null)
            {
                return new PropertyField(property);
            }

            var owner = property.serializedObject;

            return BuildRow(property.displayName, property.tooltip, assetType,
                (VslAssetSource)sourceProperty.enumValueIndex, keyProperty.stringValue,
                (nextSource, nextKey) =>
                {
                    owner.Update();
                    sourceProperty.enumValueIndex = (int)nextSource;
                    keyProperty.stringValue = nextKey ?? string.Empty;
                    owner.ApplyModifiedProperties();
                });
        }

        #endregion

        #region UI

        /// <summary>
        /// Label, object field, locator. <paramref name="apply"/> receives the new source and key,
        /// which are <see cref="VslAssetSource.None"/> and null when the field is cleared.
        /// </summary>
        private static VisualElement BuildRow(string label, string tooltip, Type assetType, VslAssetSource source, string key, Action<VslAssetSource, string> apply)
        {
            var row = new VisualElement { style = { marginTop = 1, marginBottom = 1 } };

            var line = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center },
            };
            row.Add(line);

            line.Add(new Label(label)
            {
                tooltip = string.IsNullOrEmpty(tooltip)
                    ? $"A lazy reference to a {assetType.Name}: the locator is stored, and the asset loads only when asked for."
                    : tooltip,
                style = { minWidth = 120, flexShrink = 0, unityTextAlign = TextAnchor.MiddleLeft },
            });

            // A component is picked as the prefab that carries it; the locator names the prefab and
            // the runtime narrows back to the component when it loads.
            var pickType = typeof(Component).IsAssignableFrom(assetType) ? typeof(GameObject) : assetType;

            var objectField = new ObjectField
            {
                objectType = pickType,
                allowSceneObjects = false,
                value = Resolve(source, key, pickType),
                style = { flexGrow = 1f, flexBasis = 0f, marginLeft = 0, marginRight = 0 },
            };
            line.Add(objectField);

            var locator = new Label
            {
                style =
                {
                    fontSize = 10,
                    opacity = 0.6f,
                    marginLeft = 124,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    unityTextAlign = TextAnchor.MiddleLeft,
                },
            };
            row.Add(locator);
            ShowLocator(locator, source, key, objectField.value == null);

            // What is stored right now, which is not always what the object field shows: a locator
            // whose asset has gone leaves the field empty while the key is still there.
            var storedSource = source;
            var storedKey = key;

            void Clear()
            {
                storedSource = VslAssetSource.None;
                storedKey = null;
                objectField.SetValueWithoutNotify(null);
                apply(VslAssetSource.None, null);
                ShowLocator(locator, VslAssetSource.None, null, false);
            }

            objectField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == null)
                {
                    Clear();
                    return;
                }

                if (!VslAssetLocator.TryGetKey(evt.newValue, out var nextSource, out var nextKey))
                {
                    // Nothing durable to write - a scene object, or an asset outside Resources with
                    // no Addressables settings to publish into. Put the field back rather than
                    // storing a reference that could never load.
                    Debug.LogWarning($"{evt.newValue.name} has no locator: it is not in a Resources folder and cannot be made addressable, so an {nameof(AssetRef<Object>)} cannot point at it.");
                    objectField.SetValueWithoutNotify(evt.previousValue);
                    return;
                }

                storedSource = nextSource;
                storedKey = nextKey;
                apply(nextSource, nextKey);
                ShowLocator(locator, nextSource, nextKey, false);
            });

            // Emptying an object field that is already empty raises nothing, so a locator pointing at
            // an asset this project no longer has could never be cleared through the field. This is
            // the way out: it clears what is stored rather than what is shown.
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Set Null", _ => Clear(), _ =>
                    storedSource != VslAssetSource.None || !string.IsNullOrEmpty(storedKey)
                        ? DropdownMenuAction.Status.Normal
                        : DropdownMenuAction.Status.Disabled);
            }));

            return row;
        }

        private static void ShowLocator(Label label, VslAssetSource source, string key, bool missing)
        {
            var isSet = source != VslAssetSource.None && !string.IsNullOrEmpty(key);
            label.style.display = isSet ? DisplayStyle.Flex : DisplayStyle.None;
            if (!isSet)
            {
                return;
            }

            var text = $"{source.ToString().ToLowerInvariant()}:{key}";
            label.text = missing ? $"{text}  (not found)" : text;
            label.tooltip = missing
                ? "The locator is stored, but no asset answers to it in this project. It will load as nothing."
                : "What the document stores. The asset is loaded from this when it is needed.";
            label.style.color = missing ? new StyleColor(new Color(0.9f, 0.6f, 0.2f)) : new StyleColor(StyleKeyword.Null);
        }

        /// <summary>The asset behind a locator, for display. Through the editor provider, so nothing is built or loaded through Addressables.</summary>
        private static Object Resolve(VslAssetSource source, string key, Type pickType)
        {
            if (source == VslAssetSource.None || string.IsNullOrEmpty(key))
            {
                return null;
            }

            return VslAssetLocator.TryLoad(source, key, pickType, out var obj) ? obj : null;
        }

        #endregion

        #region Reflection

        /// <summary>The <c>T</c> of an <c>AssetRef&lt;T&gt;</c>, or null when the type is not one.</summary>
        private static Type AssetTypeOf(Type refType)
        {
            if (refType is not { IsGenericType: true } || refType.GetGenericTypeDefinition() != typeof(AssetRef<>))
            {
                return null;
            }

            return refType.GetGenericArguments()[0];
        }

        /// <summary>Unwraps the element type of an array or list field, since Unity hands the drawer the field's own type.</summary>
        private static Type ElementType(Type fieldType)
        {
            if (fieldType == null)
            {
                return null;
            }

            if (fieldType.IsArray)
            {
                return fieldType.GetElementType();
            }

            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                return fieldType.GetGenericArguments()[0];
            }

            return fieldType;
        }

        /// <summary>Source and key of a boxed <c>AssetRef&lt;T&gt;</c>, whatever its <c>T</c>.</summary>
        private static (VslAssetSource source, string key) Read(object boxed)
        {
            if (boxed == null)
            {
                return (VslAssetSource.None, null);
            }

            var type = boxed.GetType();
            var source = type.GetProperty(nameof(AssetRef<Object>.Source))?.GetValue(boxed) is VslAssetSource s ? s : VslAssetSource.None;
            var key = type.GetProperty(nameof(AssetRef<Object>.Key))?.GetValue(boxed) as string;
            return (source, key);
        }

        #endregion
    }
}
