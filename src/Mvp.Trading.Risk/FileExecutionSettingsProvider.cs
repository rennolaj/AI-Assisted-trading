using System.Text.Json;

namespace Mvp.Trading.Risk;

/// <summary>
/// Loads execution settings from config/execution.json.
/// </summary>
public sealed class FileExecutionSettingsProvider : IExecutionSettingsProvider
{
    private const string ConfigFileName = "execution.json";
    private readonly string _configPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lazy<ExecutionSettings> _settings;

    public FileExecutionSettingsProvider()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "config", ConfigFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _settings = new Lazy<ExecutionSettings>(LoadSettings);
    }

    public ExecutionSettings GetSettings() => _settings.Value;

    private ExecutionSettings LoadSettings()
    {
        if (!File.Exists(_configPath))
        {
            throw new FileNotFoundException("Execution config file not found.", _configPath);
        }

        var json = File.ReadAllText(_configPath);
        var settings = JsonSerializer.Deserialize<ExecutionSettings>(json, _jsonOptions);
        if (settings is null)
        {
            throw new InvalidOperationException($"Execution config file '{_configPath}' is invalid.");
        }

        return settings;
    }
}
