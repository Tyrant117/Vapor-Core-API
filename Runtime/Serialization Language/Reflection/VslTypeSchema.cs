using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Serialization
{
    /// <summary>
    /// One serialized member: the name it is written under, how to get and set it, and the formatter
    /// for its type.
    /// </summary>
    public sealed class VslMember
    {
        private readonly FieldInfo _field;
        private readonly PropertyInfo _property;
        private IVslFormatter _formatter;

        public string Name { get; }

        public string Comment { get; }

        public Type MemberType { get; }

        /// <summary>The profiles this member belongs to; <see cref="VslProfiles.All"/> unless restricted with <see cref="VslProfileAttribute"/>.</summary>
        public VslProfiles Profiles { get; }

        internal VslMember(string name, string comment, FieldInfo field, PropertyInfo property, VslProfiles profiles = VslProfiles.All)
        {
            Name = name;
            Comment = comment;
            _field = field;
            _property = property;
            Profiles = profiles;
            MemberType = field != null ? field.FieldType : property.PropertyType;
        }

        /// <summary>True when the member takes part in a read or write filtered to <paramref name="active"/>.</summary>
        public bool IsIn(VslProfiles active) => (Profiles & active) != 0;

        /// <summary>Resolved on first use, so a type that contains itself does not recurse at build time.</summary>
        public IVslFormatter Formatter => _formatter ??= VslFormatterRegistry.Get(MemberType);

        public object GetValue(object instance) =>
            _field != null ? _field.GetValue(instance) : _property.GetValue(instance);

        public void SetValue(object instance, object value)
        {
            if (_field != null)
            {
                _field.SetValue(instance, value);
                return;
            }

            _property.SetValue(instance, value);
        }
    }

    /// <summary>
    /// The cached list of members VSL serializes for a type, after applying the opt-in and
    /// <see cref="VslSerializableAttribute"/> policies.
    /// </summary>
    // Reset by ResetCache below rather than automatically; see the note there.
    [NoAutoStaticsCleanup]
    public sealed partial class VslTypeSchema
    {
        private const BindingFlags DeclaredInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly ConcurrentDictionary<Type, VslTypeSchema> s_Schemas =
            new ConcurrentDictionary<Type, VslTypeSchema>();

        private readonly Dictionary<string, VslMember> _byName;
        private int _allScalar; // 0 unknown, 1 yes, 2 no

        public Type Type { get; }

        public VslMember[] Members { get; }

        private VslTypeSchema(Type type, VslMember[] members)
        {
            Type = type;
            Members = members;

            _byName = new Dictionary<string, VslMember>(members.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                // Indexed by the normalised name so '_hp', 'hp' and 'm_Hp' all find the same member.
                var key = VslNames.Normalize(member.Name.AsSpan()).ToString();
                AddName(key, member);
                AddName(member.Name, member);
            }
        }

        private void AddName(string name, VslMember member)
        {
            if (_byName.TryGetValue(name, out var existing) && existing != member)
            {
                throw new VslException(
                    $"{Type.Name} has ambiguous VSL members '{existing.Name}' and '{member.Name}'. " +
                    "Give one of them a distinct [VslName].");
            }

            _byName[name] = member;
        }

        // Not [AutoStaticsCleanup]: that guarantees Clear() only for the collection types it names, and
        // a readonly ConcurrentDictionary is not among them.
        [OnEnteringPlayMode]
        private static void ResetCache() => s_Schemas.Clear();

        public static VslTypeSchema Get(Type type) =>
            s_Schemas.TryGetValue(type, out var schema) ? schema : s_Schemas.GetOrAdd(type, Build(type));

        /// <summary>Finds a member by the name that appeared in the document.</summary>
        public VslMember Find(ReadOnlySpan<char> name)
        {
            var key = name.ToString();
            if (_byName.TryGetValue(key, out var member))
            {
                return member;
            }

            return _byName.TryGetValue(VslNames.Normalize(name).ToString(), out member) ? member : null;
        }

        /// <summary>True when this type is small and flat enough to write on one line.</summary>
        public bool PrefersInline(VslContext context)
        {
            if (Members.Length == 0 || Members.Length > context.Options.InlineMemberLimit)
            {
                return false;
            }

            if (_allScalar == 0)
            {
                _allScalar = 1;
                foreach (var member in Members)
                {
                    if (!member.Formatter.IsScalar)
                    {
                        _allScalar = 2;
                        break;
                    }
                }
            }

            return _allScalar == 1;
        }

        private static VslTypeSchema Build(Type type)
        {
            var members = new List<VslMember>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Base-first so inherited members read in declaration order, and so a derived member
            // shadowing a base one wins the plain name.
            var hierarchy = new List<Type>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                hierarchy.Insert(0, current);
            }

            foreach (var level in hierarchy)
            {
                var unityRules = level.IsDefined(typeof(VslSerializableAttribute), false);

                foreach (var field in level.GetFields(DeclaredInstance))
                {
                    if (!ShouldSerialize(field, unityRules))
                    {
                        continue;
                    }

                    members.Add(CreateMember(field, null, claimed));
                }

                foreach (var property in level.GetProperties(DeclaredInstance))
                {
                    if (!ShouldSerialize(property))
                    {
                        continue;
                    }

                    members.Add(CreateMember(null, property, claimed));
                }
            }

            return new VslTypeSchema(type, members.ToArray());
        }

        private static bool ShouldSerialize(FieldInfo field, bool unityRules)
        {
            if (field.IsLiteral || field.IsInitOnly || field.IsStatic)
            {
                return false;
            }

            if (field.IsDefined(typeof(VslIgnoreAttribute), true))
            {
                return false;
            }

            // An explicit opt-in wins, including over [NonSerialized].
            if (field.IsDefined(typeof(VslSerializeAttribute), true))
            {
                return true;
            }

            if (!unityRules || field.IsDefined(typeof(NonSerializedAttribute), true))
            {
                RequireSerializeAlongsideReference(field);
                return false;
            }

            // Unity's own rule: public fields, plus non-public fields carrying [SerializeField].
            if (field.IsPublic || field.IsDefined(typeof(SerializeField), true))
            {
                return true;
            }

            RequireSerializeAlongsideReference(field);
            return false;
        }

        private static bool ShouldSerialize(PropertyInfo property)
        {
            if (property.IsDefined(typeof(VslIgnoreAttribute), true))
            {
                return false;
            }

            // Properties are never picked up by the type-level policy — Unity does not serialize them
            // either, and a computed property can have side effects. They must be asked for.
            if (!property.IsDefined(typeof(VslSerializeAttribute), true))
            {
                RequireSerializeAlongsideReference(property);
                return false;
            }

            if (property.GetIndexParameters().Length > 0)
            {
                return false;
            }

            if (property.CanRead && property.CanWrite)
            {
                return true;
            }

            throw new VslException(
                $"{property.DeclaringType?.Name}.{property.Name} is marked [VslSerialize] but needs both a getter and a setter.");
        }

        /// <summary>
        /// Rejects a member marked <see cref="VslSerializeReferenceAttribute"/> that is not serialized.
        /// </summary>
        /// <remarks>
        /// Reached only on the paths that decided to skip a member. Declaring a slot polymorphic and
        /// then not serializing it is always a mistake, and a silent one: the picker appears in the
        /// inspector, the value is chosen, and the document never records it.
        /// </remarks>
        private static void RequireSerializeAlongsideReference(MemberInfo member)
        {
            if (!member.IsDefined(typeof(VslSerializeReferenceAttribute), true))
            {
                return;
            }

            throw new VslException(
                $"{member.DeclaringType?.Name}.{member.Name} is marked [VslSerializeReference] but not [VslSerialize], " +
                "so it would not be written. Add [VslSerialize] beside it.");
        }

        private static VslMember CreateMember(FieldInfo field, PropertyInfo property, HashSet<string> claimed)
        {
            var member = (MemberInfo)field ?? property;
            var rename = member.GetCustomAttribute<VslNameAttribute>(true);
            var comment = member.GetCustomAttribute<VslCommentAttribute>(true);

            string name;
            if (rename != null && !string.IsNullOrEmpty(rename.Name))
            {
                name = rename.Name;
            }
            else
            {
                // Drop the backing-field prefix and lower the first letter, so 'Name', '_health' and
                // 'm_Speed' all read as 'name', 'health', 'speed'. Without this the document mixes
                // conventions depending on how each field happened to be declared, which is exactly
                // the kind of inconsistency that makes a format harder to generate against.
                var normalized = ToCamelCase(VslNames.Normalize(member.Name.AsSpan()));
                name = normalized.Length > 0 && !claimed.Contains(normalized) ? normalized : member.Name;
            }

            claimed.Add(name);
            var profile = member.GetCustomAttribute<VslProfileAttribute>(true);
            return new VslMember(name, comment?.Comment, field, property, profile?.Profiles ?? VslProfiles.All);
        }

        private static string ToCamelCase(ReadOnlySpan<char> name)
        {
            if (name.Length == 0 || !char.IsUpper(name[0]))
            {
                return name.ToString();
            }

            // Leave an acronym alone: 'ID' should not become 'iD'.
            if (name.Length > 1 && char.IsUpper(name[1]))
            {
                return name.ToString();
            }

            Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
            name.CopyTo(buffer);
            buffer[0] = char.ToLowerInvariant(buffer[0]);
            return new string(buffer);
        }
    }
}
