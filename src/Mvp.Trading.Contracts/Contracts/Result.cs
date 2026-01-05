using System.Collections.Generic;

namespace Mvp.Trading.Contracts;

/// <summary>
/// Wraps a success/failure outcome for cross-service calls.
/// </summary>
public sealed record Result<T>(bool Ok, T? Value, Error? Error);

/// <summary>
/// Describes a structured error with an optional metadata bag.
/// </summary>
public sealed record Error(string Code, string Message, IReadOnlyDictionary<string, string?>? Meta);
