using System;
using System.Collections.Generic;
using System.IO;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor
{
    /// <summary>
    /// Finds the <c>.cs</c> file a type is declared in, and builds the button that opens it.
    /// </summary>
    /// <remarks>
    /// Every window that inspects a plain C# object eventually wants "show me the class", and the
    /// lookup is neither obvious nor cheap - so it lives once, here, rather than in each of them.
    /// Statics are cleaned on code load, which is exactly when a file could have moved or been renamed.
    /// </remarks>
    [AutoStaticsCleanup]
    public static partial class TypeScriptLocator
    {
        private static Dictionary<Type, MonoScript> s_Cache;

        /// <summary>
        /// The script a type is declared in, or null when there is nothing to open - the type came
        /// from a dll, or lives in a file named after something else.
        /// </summary>
        /// <remarks>
        /// Searched by file name, because these are plain classes rather than
        /// <see cref="ScriptableObject"/>s: Unity resolves <see cref="MonoScript.GetClass"/> for few of
        /// them, so the name is usually all there is to go on. Where it does resolve, it is what
        /// decides between two files of the same name.
        /// </remarks>
        public static MonoScript Find(Type type)
        {
            if (type == null)
            {
                return null;
            }

            s_Cache ??= new Dictionary<Type, MonoScript>();
            if (s_Cache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
            {
                // Foo`1 is declared in Foo.cs.
                name = name[..tick];
            }

            MonoScript found = null;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.Ordinal))
                {
                    // The filter matches on words in the name, so most hits are some other file.
                    continue;
                }

                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null)
                {
                    continue;
                }

                var declared = script.GetClass();
                if (declared == type)
                {
                    found = script;
                    break;
                }

                // A file Unity resolved to a different type is the wrong one; an unresolved file is a
                // candidate, and the only one this can offer unless a resolved match turns up later.
                found ??= declared == null ? script : null;
            }

            s_Cache[type] = found;
            return found;
        }

        /// <summary>
        /// The C# script icon: the one the found asset carries, so a button matches what the project
        /// browser shows for the same file, and the built-in one when there is no asset to ask.
        /// </summary>
        public static Texture2D Icon(MonoScript script)
        {
            var icon = script == null ? null : AssetPreview.GetMiniThumbnail(script);
            return icon != null ? icon : EditorGUIUtility.IconContent("cs Script Icon")?.image as Texture2D;
        }

        /// <summary>
        /// A small header button that opens the type's script, disabled when there is none to open.
        /// </summary>
        /// <remarks>
        /// Sized to sit beside the other header buttons rather than to a style sheet, because the
        /// windows that use it draw their chrome inline. Restyle the returned button where a call site
        /// wants something different.
        /// </remarks>
        public static Button CreateButton(Type type, string label = null)
        {
            var script = Find(type);

            var button = new Button(() =>
            {
                if (script != null)
                {
                    AssetDatabase.OpenAsset(script);
                }
            })
            {
                tooltip = script != null
                    ? $"Open {AssetDatabase.GetAssetPath(script)}."
                    : $"No script to open: {label ?? type?.Name} has no .cs file in the project.",
                style =
                {
                    height = 18,
                    minWidth = 22,
                    marginLeft = 4,
                    marginTop = 0,
                    marginBottom = 0,
                    marginRight = 0,
                    paddingLeft = 4,
                    paddingRight = 4,
                    flexShrink = 0f,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                },
            };

            button.SetEnabled(script != null);

            var icon = Icon(script);
            if (icon != null)
            {
                button.Add(new VisualElement
                {
                    style = { backgroundImage = new StyleBackground(icon), width = 14, height = 14, flexShrink = 0f },
                });
            }
            else
            {
                button.text = "C#";
                button.style.fontSize = 10;
            }

            return button;
        }
    }
}
