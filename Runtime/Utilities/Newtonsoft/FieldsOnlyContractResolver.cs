using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Vapor
{
    public class FieldsOnlyContractResolver : DefaultContractResolver
    {
        protected override List<MemberInfo> GetSerializableMembers(Type objectType)
        {
            var members = base.GetSerializableMembers(objectType);
            members.AddRange(GetMissingMembers(objectType, members));
            return members;
        }
        
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            // check member types
            if (member.MemberType == MemberTypes.Property)
            {
                property.ShouldSerialize = _ => false; // Ignore all properties
            }
            
            if (member.GetCustomAttribute<SerializeField>() != null)
            {
                property.Ignored = false;
                property.Writable = CanWriteMemberWithSerializeField(member);
                property.Readable = CanReadMemberWithSerializeField(member);
                property.HasMemberAttribute = true;
            }

            return property;
        }
        
        private static IEnumerable<MemberInfo> GetMissingMembers(Type type, List<MemberInfo> alreadyAdded)
        {
            return type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Cast<MemberInfo>()
                .Concat(type.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                .Where(o => o.GetCustomAttribute<SerializeField>() != null
                            && !alreadyAdded.Contains(o));
        }
        
        private static bool CanReadMemberWithSerializeField(MemberInfo member)
        {
            if (member is PropertyInfo property)
            {
                return property.CanRead;
            }
            else
            {
                return true;
            }
        }

        private static bool CanWriteMemberWithSerializeField(MemberInfo member)
        {
            if (member is PropertyInfo property)
            {
                return property.CanWrite;
            }
            else
            {
                return true;
            } 
        }
    }
}
