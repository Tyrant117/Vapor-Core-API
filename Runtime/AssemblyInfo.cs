using System.Runtime.CompilerServices;
using Vapor;

[assembly: TypeCache]

// The network object layer keeps its replication state internal — NetworkObjectId, IsServer and the
// dirty queue are set by the framework, not by callers. Tests have to stand an object up in those
// states to exercise anything that replicates.
[assembly: InternalsVisibleTo("vapor.core.tests")]
