using System;
using JetBrains.Annotations;
using UnityEngine;

namespace Vapor.GameplayTags
{
    [AttributeUsage(AttributeTargets.Field)]
    public class GameplayTagDrawerAttribute : PropertyAttribute
    {
        public bool LeavesOnly { get; }
        public bool Allowed { get; }
        public string[] FilteredParents { get; }

        /// <summary>
        /// Metadata for the gameplay tags to narrow down the tag branches that are shown for selection
        /// </summary>
        /// <param name="leavesOnly">If true, only the leaf tags will be selectable</param>
        /// <param name="allowed">If true, the filtered parents will only show the ones select, otherwise it will exclude only the ones selected</param>
        /// <param name="filteredParents">If filtered parents are not null, only gameplay tags will be filtered based on the allowed parameter</param>
        public GameplayTagDrawerAttribute(bool leavesOnly, bool allowed, params string[] filteredParents)
        {
            LeavesOnly = leavesOnly;
            Allowed = allowed;
            FilteredParents = filteredParents;
        }

        /// <summary>
        /// Metadata for the gameplay tags to narrow down the tag branches that are shown for selection
        /// </summary>
        /// <param name="leavesOnly">If true, only the leaf tags will be selectable</param>
        /// <param name="allowed">If true, the filtered parents will only show the ones select, otherwise it will exclude only the ones selected</param>
        /// <param name="filteredParents">If filtered parents are not null, only gameplay tags will be filtered based on the allowed parameter</param>
        public GameplayTagDrawerAttribute(bool leavesOnly, bool allowed, params GameplayTagDefaultCategories[] filteredParents)
        {
            LeavesOnly = leavesOnly;
            Allowed = allowed;
            if (filteredParents != null)
            {
                FilteredParents = new string[filteredParents.Length];
                for (var i = 0; i < filteredParents.Length; i++)
                {
                    FilteredParents[i] = GameplayTagCategories.GetNameForCategory(filteredParents[i]);
                }
            }
        }

        /// <summary>
        /// Metadata for the gameplay tags to narrow down the tag branches that are shown for selection
        /// </summary>
        /// <param name="allowed">If true, the filtered parents will only show the ones select, otherwise it will exclude only the ones selected</param>
        /// <param name="filteredParents">If filtered parents are not null, only gameplay tags will be filtered based on the allowed parameter</param>
        public GameplayTagDrawerAttribute(bool allowed, params string[] filteredParents)
        {
            LeavesOnly = false;
            Allowed = allowed;
            FilteredParents = filteredParents;
        }

        /// <summary>
        /// Metadata for the gameplay tags to narrow down the tag branches that are shown for selection
        /// </summary>
        /// <param name="allowed">If true, the filtered parents will only show the ones select, otherwise it will exclude only the ones selected</param>
        /// <param name="filteredParents">If filtered parents are not null, only gameplay tags will be filtered based on the allowed parameter</param>
        public GameplayTagDrawerAttribute(bool allowed, params GameplayTagDefaultCategories[] filteredParents)
        {
            LeavesOnly = false;
            Allowed = allowed;
            if (filteredParents != null)
            {
                FilteredParents = new string[filteredParents.Length];
                for (var i = 0; i < filteredParents.Length; i++)
                {
                    FilteredParents[i] = GameplayTagCategories.GetNameForCategory(filteredParents[i]);
                }
            }
        }
    }
}