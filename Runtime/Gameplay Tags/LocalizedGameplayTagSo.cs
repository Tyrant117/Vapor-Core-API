using System;
using UnityEngine;
using UnityEngine.Localization;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor.GameplayTags
{
    [DatabaseKeyValuePair(true, "LocalizedGameplayTag"), KeyOptions(useNameAsGuid: true, category: GameplayTagUtility.CATEGORY_NAME)]
    public class LocalizedGameplayTagSo : GameplayTagSo
    {
        [BoxGroup("Localization")]
        public LocalizedString LocalizedName;
        [BoxGroup("Localization")]
        public Sprite Icon;
    }
}