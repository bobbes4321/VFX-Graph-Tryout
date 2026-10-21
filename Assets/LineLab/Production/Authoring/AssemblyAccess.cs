using System.Runtime.CompilerServices;

// Unity compiles editor validation and graph compilation into a separate assembly.
// Those trusted tools may inspect the owned native storage. Public runtime callers
// receive read-only borrowed views and may not mutate or dispose catalog tables.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
