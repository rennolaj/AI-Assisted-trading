using System;

namespace Mvp.Trading.Elliott;

/// <summary>
/// ZigZag pivot type.
/// </summary>
public enum PivotType
{
    High,
    Low
}

/// <summary>
/// Pivot point extracted from a candle series.
/// </summary>
public sealed record PivotPoint(int Index, DateTimeOffset TimeUtc, decimal Price, PivotType Type);
