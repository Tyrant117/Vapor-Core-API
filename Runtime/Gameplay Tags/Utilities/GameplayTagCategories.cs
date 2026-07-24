using System;

namespace Vapor.GameplayTags
{
    public enum GameplayTagDefaultCategories
    {
        Socket,
        AnimationLayer,
        Event,
        Input,
        Cue,
        Addressable,
        Montage,
        Attribute,
        Effect,
        Ability,
        Items,
        Entity,
        Loot,
        Spell,
        Item,
        Movement,
        GameplayTagData,
        Vapor,
        Profession,
        Skill,
        Recipe,
        Perk,
        WorldSettings,
    }

    public static class GameplayTagCategories
    {
        public const string SOCKET = "Socket";
        public const string ANIMATION_LAYER = "AnimationLayer";
        public const string EVENT = "Event";
        public const string INPUT = "Input";
        public const string CUE = "Cue";
        public const string ADDRESSABLE = "Addressable";
        public const string MONTAGE = "Montage";
        public const string ATTRIBUTE = "Attribute";
        public const string EFFECT = "Effect";
        public const string ABILITY = "Ability";
        public const string ITEMS = "Items";
        public const string ENTITY = "Entity";
        public const string LOOT = "Loot";
        public const string SPELL = "Spell";
        public const string ITEM = "Item";
        public const string MOVEMENT = "Movement";
        public const string GAMEPLAY_TAG_DATA = "GameplayTagData";
        public const string VAPOR = "Vapor";
        public const string PROFESSION = "Profession";
        public const string SKILL = "Skill";
        public const string RECIPE = "Recipe";
        public const string PERK = "Perk";
        public const string WORLD_SETTINGS = "WorldSettings";

        public static string GetNameForCategory(GameplayTagDefaultCategories category)
        {
            return category switch
            {
                GameplayTagDefaultCategories.Socket => SOCKET,
                GameplayTagDefaultCategories.AnimationLayer => ANIMATION_LAYER,
                GameplayTagDefaultCategories.Event => EVENT,
                GameplayTagDefaultCategories.Input => INPUT,
                GameplayTagDefaultCategories.Cue => CUE,
                GameplayTagDefaultCategories.Addressable => ADDRESSABLE,
                GameplayTagDefaultCategories.Montage => MONTAGE,
                GameplayTagDefaultCategories.Attribute => ATTRIBUTE,
                GameplayTagDefaultCategories.Effect => EFFECT,
                GameplayTagDefaultCategories.Ability => ABILITY,
                GameplayTagDefaultCategories.Items => ITEMS,
                GameplayTagDefaultCategories.Entity => ENTITY,
                GameplayTagDefaultCategories.Loot => LOOT,
                GameplayTagDefaultCategories.Spell => SPELL,
                GameplayTagDefaultCategories.Item => ITEM,
                GameplayTagDefaultCategories.Movement => MOVEMENT,
                GameplayTagDefaultCategories.GameplayTagData => GAMEPLAY_TAG_DATA,
                GameplayTagDefaultCategories.Vapor => VAPOR,
                GameplayTagDefaultCategories.Profession => PROFESSION,
                GameplayTagDefaultCategories.Skill => SKILL,
                GameplayTagDefaultCategories.Recipe => RECIPE,
                GameplayTagDefaultCategories.Perk => PERK,
                GameplayTagDefaultCategories.WorldSettings => WORLD_SETTINGS,
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
            };
        }
    }
}
