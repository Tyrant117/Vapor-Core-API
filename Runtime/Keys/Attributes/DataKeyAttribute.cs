using System;

namespace Vapor.Keys
{
    /// <summary>
    /// Hints the Vapor Keys Rider/ReSharper plugin that a plain <see cref="string"/> or <see cref="uint"/>
    /// member holds a data key, so the IDE offers autocomplete and validation against the generated key
    /// manifests (see the *.keys.tsv files under Assets/Vapor/Keys/Definitions/Generated).
    /// <para>
    /// Only needed where the member type is a bare primitive that type-based detection cannot associate
    /// with a key set, e.g. <c>Actor.TryGetAttribute([DataKey(typeof(AttributeData))] uint attributeId)</c>.
    /// Members typed as <c>GameplayTag</c> / <c>KeyDropdownValue</c> or passed to
    /// <c>DataRegistry&lt;T&gt;.Get/TryGet</c> are detected automatically and need no annotation.
    /// </para>
    /// This attribute carries no runtime behaviour; it exists purely as IDE metadata and is safe to ship.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class DataKeyAttribute : Attribute
    {
        /// <summary>
        /// The key category or shared name prefix to complete against. An exact "category" column match in
        /// keys.index.tsv (e.g. "Attributes") wins; otherwise the value is treated as a dotted display-name
        /// prefix (e.g. "Attribute" offers "Attribute.Armor.Melee", …). Use this or <see cref="DataType"/>.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// The data type whose keys to complete against (e.g. <c>typeof(AttributeData)</c>). Resolved through
        /// the "dataTypeSimpleName"/"dataTypeFullName" columns in keys.index.tsv; a type with no index row
        /// falls back to its simple name as a display-name prefix. Alternative to <see cref="Category"/>.
        /// </summary>
        public Type DataType { get; }

        /// <summary>Complete against the keys in the given <paramref name="categoryOrNamePrefix"/>.</summary>
        public DataKeyAttribute(string categoryOrNamePrefix)
        {
            Category = categoryOrNamePrefix;
        }

        /// <summary>Complete against the keys generated for the given <paramref name="dataType"/>.</summary>
        public DataKeyAttribute(Type dataType)
        {
            DataType = dataType;
        }
    }
}
