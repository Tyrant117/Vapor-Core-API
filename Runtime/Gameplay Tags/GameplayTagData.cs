using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.ResourceManagement.AsyncOperations;
using Vapor.Unsafe;

namespace Vapor.GameplayTags
{
    public class GameplayTagData : IData
    {
        public string Name { get; }
        public uint Key { get; }
        
        
        public string EditorTooltip { get; private set; }
        public LocalizedString DisplayName { get; private set; }
        public string IconAddressableName { get; private set; }


        public GameplayTagData(string name)
        {
            Name = name;
            Key = Name.Hash32();
        }

        public GameplayTagData WithEditorTooltip(string editorTooltip)
        {
            EditorTooltip = editorTooltip;
            return this;
        }
        
        public GameplayTagData WithLocalization(LocalizedString displayName, string iconAddressableName)
        {
            DisplayName = displayName;
            IconAddressableName = iconAddressableName;
            return this;
        }
        
        public Sprite GetIcon(out AsyncOperationHandle<Sprite> handle)
        {
            if (!IconAddressableName.EmptyOrNull())
            {
                return AddressableAssetUtility.Load(IconAddressableName, out handle);
            }

            handle = default;
            return null;
        }
    }
}