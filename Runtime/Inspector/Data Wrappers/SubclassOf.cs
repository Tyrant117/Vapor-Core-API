using System;
using System.Collections.Generic;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor
{
    [Serializable, DrawWithVapor(UIGroupType.Vertical)]
    public class SubclassOf<T>
    {
        [SerializeField, TypeSelector("@GetSubclassType", flattenCategories: true), InheritedLabel]
        private string _assemblyQualifiedType;
        
        [NonSerialized]
        private Type _type;

        public Type ResolveType()
        {
            return _type ??= _assemblyQualifiedType != null ? Type.GetType(_assemblyQualifiedType) : null;
        }

        private IEnumerable<Type> GetSubclassType()
        {
            yield return typeof(T);
        }
    }
}
