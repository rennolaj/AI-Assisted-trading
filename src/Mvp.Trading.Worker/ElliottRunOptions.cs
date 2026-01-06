using System.Collections.Generic;

namespace Mvp.Trading.Worker;

/// <summary>
/// Configuration binding for Elliott candidate generation.
/// </summary>
public sealed class ElliottRunOptions
{
    public string BaseTimeframe { get; set; } = "M15";

    public ElliottParametersOptions Parameters { get; set; } = new();

    public decimal TickSizeFallback { get; set; } = 0m;

    public Dictionary<string, decimal> TickSizeOverrides { get; set; } = new();
}

/// <summary>
/// Elliott parameter configuration values.
/// </summary>
public sealed class ElliottParametersOptions
{
    public string PivotMethod { get; set; } = "ZigZag";

    public int Depth { get; set; } = 12;

    public decimal DeviationPct { get; set; } = 5m;

    public int MaxCandidates { get; set; } = 10;
}
