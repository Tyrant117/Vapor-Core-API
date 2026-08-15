using System.Runtime.CompilerServices;

// Vapor.Networking is a foundation assembly: it depends on nothing else in Vapor so that every other
// Vapor assembly (core runtime, gameplay framework, RPG) can depend on it without a cycle. Anything it
// needs from elsewhere — hashing, tags — is either duplicated here in miniature or registered into it
// from above (formatters for GameplayTag, for example, are registered by vapor.core.runtime).
//
// Replication state (ids, roles, dirty queues) is set by the framework, not by callers; tests have to
// stand objects up in those states to exercise anything that replicates.
[assembly: InternalsVisibleTo("vapor.core.tests")]
[assembly: InternalsVisibleTo("vapor.core.networking.utp")]
