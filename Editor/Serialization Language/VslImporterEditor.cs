using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine.UIElements;

namespace VaporEditor.Serialization
{
    /// <summary>
    /// Shows a <c>.vsl</c> document's text in the inspector.
    /// </summary>
    /// <remarks>
    /// A <see cref="ScriptedImporter"/> otherwise draws an empty inspector, which makes selecting a
    /// data document in the project window useless. The text is read-only here on purpose: the
    /// <c>Vapor Data/Data Types</c> window is the editing surface, and two live editors over the same
    /// file would race each other.
    /// </remarks>
    [CustomEditor(typeof(VslImporter))]
    public sealed class VslImporterEditor : ScriptedImporterEditor
    {
        public override bool showImportedObject => false;

        protected override bool needsApplyRevert => false;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var path = ((AssetImporter)target).assetPath;
            var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;

            var field = new TextField
            {
                multiline = true,
                isReadOnly = true,
                value = text,
            };
            field.style.whiteSpace = WhiteSpace.Normal;
            field.style.marginTop = 4;
            field.style.marginBottom = 4;
            root.Add(field);

            return root;
        }
    }
}
