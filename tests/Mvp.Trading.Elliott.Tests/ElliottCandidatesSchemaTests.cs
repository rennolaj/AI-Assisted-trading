using System;
using System.IO;
using System.Text.Json;
using Json.Schema;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Schema validation for ElliottCandidates.
/// </summary>
public sealed class ElliottCandidatesSchemaTests
{
    [Fact]
    public void ElliottCandidates_MatchesSchema()
    {
        var candidate = new ElliottCandidate(
            "cand-001",
            "IMPULSE",
            "W5END",
            65m,
            0.65m,
            Array.Empty<RuleViolation>(),
            new InvalidationLevels(99.5m, null));

        var payload = new ElliottCandidates(Timeframe.M15, new[] { candidate });
        var json = ElliottCandidatesJson.Serialize(payload);

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schemas", "ElliottCandidates.schema.json");
        var schema = JsonSchema.FromFile(schemaPath);

        using var doc = JsonDocument.Parse(json);
        var result = schema.Evaluate(doc.RootElement);

        Assert.True(result.IsValid);
    }
}
