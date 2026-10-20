// netstandard2.0 lacks this marker type; the compiler needs it for records and `init` setters
// (EventEnvelope, EventDefinition). Internal, so it never clashes with a consumer's own copy.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
