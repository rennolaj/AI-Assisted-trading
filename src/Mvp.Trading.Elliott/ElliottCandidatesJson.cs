using System.Text.Json;
using System.Text.Json.Serialization;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott;

/// <summary>
/// Deterministic JSON serialization for Elliott candidates.
/// </summary>
public static class ElliottCandidatesJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static string Serialize(ElliottCandidates candidates)
    {
        return JsonSerializer.Serialize(candidates, Options);
    }
}
