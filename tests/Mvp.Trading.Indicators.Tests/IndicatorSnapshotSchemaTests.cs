using System;
using System.IO;
using System.Text.Json;
using Json.Schema;
using Mvp.Trading.Contracts;
using Mvp.Trading.Indicators;
using Xunit;

namespace Mvp.Trading.Indicators.Tests;

/// <summary>
/// Schema validation tests for indicator snapshots.
/// </summary>
public sealed class IndicatorSnapshotSchemaTests
{
    [Fact]
    public void IndicatorSnapshot_MatchesSchema()
    {
        var snapshot = new IndicatorSnapshot(
            Guid.Parse("8E6C1277-7F35-4B40-8B68-8B8B6A5E7F7A"),
            Guid.Parse("2F9CE3A6-8A23-4A39-A5B2-9B5F7C2B0A36"),
            "PI_XBTUSD",
            IndicatorDefaults.ScalpingMode,
            "LONG",
            Timeframe.M30,
            Timeframe.H2,
            DateTimeOffset.Parse("2026-01-05T12:00:00Z"),
            DateTimeOffset.Parse("2026-01-05T12:00:00Z"),
            new IndicatorParameters(14, 14, 12, 26, 9, new VolumeRule("SMA_RATIO", 20, 1.5m)),
            new[]
            {
                new TimeframeSnapshot(
                    Timeframe.M30,
                    new RsiState(35m, "NEUTRAL"),
                    new StochRsiState(42m, 38m, "NEUTRAL"),
                    new MacdState(1.2m, 1.0m, 0.2m, "BULLISH"),
                    new VolumeState(1.6m, "CONFIRMED", "SMA_RATIO:20:1.5"))
            },
            new IndicatorGates(true, true, true),
            new IndicatorConfirmations(true, true, true, true, true, true),
            new IndicatorScore(5, 100, 100, 100m),
            new IndicatorRisk("SAFE", "ALLOW", 1.25m, true, true, 4));

        var json = IndicatorSnapshotJson.Serialize(snapshot);

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schemas", "IndicatorSnapshot.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        var schema = JsonSchema.FromText(schemaText);
        using var document = JsonDocument.Parse(json);
        var result = schema.Evaluate(document.RootElement);

        Assert.True(result.IsValid);
    }
}
