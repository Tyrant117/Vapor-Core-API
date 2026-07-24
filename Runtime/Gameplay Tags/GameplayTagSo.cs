using System;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor.GameplayTags
{
    [DatabaseKeyValuePair, KeyOptions(useNameAsGuid: true, category: GameplayTagUtility.CATEGORY_NAME)]
    public class GameplayTagSo : NamedKeySo
    {
        [BoxGroup("Key")]
        public string EditorTooltip;
        
        public override Type GetKeyScriptType()
        {
            return typeof(GameplayTagSo);
        }

        public override void GenerateAdditionalKeys()
        {
#if UNITY_EDITOR
            // var oldConfig = GameplayTagUtility.GetConfig();
            // var config = new GameplayTagConfig
            // {
            //     GameplayTags = new List<GameplayTagSaveData>(),
            //     DeprecatedGameplayTags = new List<DeprecatedGameplayTagSaveData>()
            // };
            //
            // var guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(GameplayTagSo)}");
            // var currentTags = guids.Select(guid => UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayTagSo>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid))).ToList();
            //
            // config.GameplayTags = currentTags.Select(t =>
            // {
            //     UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t, out var guid, out _);
            //     return new GameplayTagSaveData { Guid = guid, Name = t.name, Key = t.Key };
            // }).ToList();
            // var missingTags = oldConfig.GameplayTags.Except(config.GameplayTags, new GameplayTagSaveDataEquality());
            // config.DeprecatedGameplayTags.AddRange(oldConfig.DeprecatedGameplayTags);
            // foreach (var missing in missingTags)
            // {
            //     config.DeprecatedGameplayTags.Add(new DeprecatedGameplayTagSaveData
            //     {
            //         Guid = missing.Guid,
            //         OldName = missing.Name,
            //         OldKey = missing.Key,
            //     });
            // }
            //
            // GameplayTagUtility.UpdateConfig(config);
#endif
        }
    }
}
