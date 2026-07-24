using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor.GameplayTags
{
    public class GameplayTagSaveDataEquality : IEqualityComparer<GameplayTagSaveData>
    {
        public bool Equals(GameplayTagSaveData x, GameplayTagSaveData y)
        {
            return x.Name == y.Name;
        }

        public int GetHashCode(GameplayTagSaveData obj)
        {
            return HashCode.Combine(obj.Name);
        }
    }

    [Serializable]
    public struct GameplayTagSaveData : IEquatable<GameplayTagSaveData>
    {
        public string Guid;
        public string Name;
        public uint Key;
        public string DeveloperTooltip;

        public bool Equals(GameplayTagSaveData other)
        {
            return Guid == other.Guid;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Guid);
        }
    }

    [Serializable]
    public struct DeprecatedGameplayTagSaveData
    {
        public string Guid;
        public string OldName;
        public uint OldKey;
    }

    [Serializable]
    public struct GameplayTagConfig
    {
        public List<GameplayTagSaveData> GameplayTags;
        public List<DeprecatedGameplayTagSaveData> DeprecatedGameplayTags;
    }

    public static class GameplayTagUtility
    {
        public const string CATEGORY_NAME = "GameplayTags";
        private const string k_MissingName = "Missing Key";
        
        public static List<DropdownModel> GetAllKeys()
        {
            return KeyUtility.GetAllDropdownModels();
            // return KeyUtility.GetAllKeysFromCategory(CATEGORY_NAME);
        }

        public static string FindName(uint multicastKey)
        {
            var foundKey = GetAllKeys().Select(dropdown => (KeyDropdownValue)dropdown.Value).FirstOrDefault(key => key.Key == multicastKey);
            return foundKey.IsNone ? k_MissingName : foundKey.DisplayName;
        }
    }
}
