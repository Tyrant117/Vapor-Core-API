using System.Runtime.CompilerServices;

// The scratchpad's store, models and path rules are internal — nothing outside this assembly has any
// business writing a handoff file or handing out a note id. The tests exercise exactly those rules,
// so they need in.
[assembly: InternalsVisibleTo("vapor.core.tests")]
