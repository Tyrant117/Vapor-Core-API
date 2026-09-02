using System.Runtime.CompilerServices;
using Vapor;
using Vapor.Serialization;

[assembly: TypeCache]

// VSL references nothing in Vapor, so it cannot ship a formatter for GameplayTag or VslRef.
// This is how it learns about them, and it runs before the registry answers its first lookup.
[assembly: VslFormatterProvider(typeof(VaporVslFormatters))]

// The network object layer keeps its replication state internal — NetworkObjectId, IsServer and the
// dirty queue are set by the framework, not by callers. Tests have to stand an object up in those
// states to exercise anything that replicates.
[assembly: InternalsVisibleTo("vapor.core.tests")]
