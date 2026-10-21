// netstandard2.0 lacks this marker type; the compiler needs it for records and `init` setters.
// Internal, so it never clashes with a consumer's own copy (or the Contract package's).
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
