using System.Runtime.CompilerServices;

// Krafs.Publicizer publicizes Assembly-CSharp at compile time; this attribute is required at runtime
// so Mono allows RaidableBases to touch private game fields the same way Harmony mods do.
[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]
[assembly: IgnoresAccessChecksTo("Assembly-CSharp-firstpass")]
[assembly: IgnoresAccessChecksTo("Facepunch.System")]
