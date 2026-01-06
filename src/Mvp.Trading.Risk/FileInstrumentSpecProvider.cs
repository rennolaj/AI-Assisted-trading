using System.Collections.Generic;
using System.Text.Json;

namespace Mvp.Trading.Risk;

/// <summary>
/// Loads instrument specifications from config/instruments.json.
/// </summary>
public sealed class FileInstrumentSpecProvider : IInstrumentSpecProvider
{
    private const string ConfigFileName = "instruments.json";
    private readonly string _configPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lazy<Dictionary<string, InstrumentSpec>> _specs;

    public FileInstrumentSpecProvider()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "config", ConfigFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _specs = new Lazy<Dictionary<string, InstrumentSpec>>(LoadSpecs);
    }

    public InstrumentSpec? GetSpec(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var specs = _specs.Value;
        return specs.TryGetValue(symbol, out var spec) ? spec : null;
    }

    private Dictionary<string, InstrumentSpec> LoadSpecs()
    {
        if (!File.Exists(_configPath))
        {
            throw new FileNotFoundException("Instrument config file not found.", _configPath);
        }

        var json = File.ReadAllText(_configPath);
        var catalog = JsonSerializer.Deserialize<InstrumentSpecCatalog>(json, _jsonOptions);
        if (catalog?.Instruments is null || catalog.Instruments.Length == 0)
        {
            throw new InvalidOperationException($"Instrument config file '{_configPath}' is invalid.");
        }

        var map = new Dictionary<string, InstrumentSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in catalog.Instruments)
        {
            if (!string.IsNullOrWhiteSpace(spec.Symbol))
            {
                map[spec.Symbol] = spec;
            }
        }

        if (map.Count == 0)
        {
            throw new InvalidOperationException($"Instrument config file '{_configPath}' contains no valid symbols.");
        }

        return map;
    }
}
