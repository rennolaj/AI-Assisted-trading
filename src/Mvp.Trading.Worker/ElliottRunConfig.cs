using Mvp.Trading.Contracts;

namespace Mvp.Trading.Worker;

/// <summary>
/// Resolved Elliott configuration used by the worker.
/// </summary>
public sealed record ElliottRunConfig(Timeframe BaseTimeframe, ElliottParameters Parameters);
