using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mvp.Trading.Indicators;

/// <summary>
/// JSON helpers for indicator snapshots.
/// </summary>
public static class IndicatorSnapshotJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }
}
