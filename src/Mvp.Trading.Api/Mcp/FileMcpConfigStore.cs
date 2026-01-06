using System.Text.Json;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Loads MCP configuration from config/mcp.json in the application directory.
/// </summary>
public sealed class FileMcpConfigStore : IMcpConfigStore
{
    private const string ConfigFileName = "mcp.json";
    private readonly string _configPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lazy<McpConfiguration> _config;

    public FileMcpConfigStore()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "config", ConfigFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _config = new Lazy<McpConfiguration>(LoadConfig);
    }

    public McpConfiguration GetConfig() => _config.Value;

    public McpToolConfig? GetToolConfig(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        var config = GetConfig();
        return config.Mcp.Tools.TryGetValue(toolName, out var tool) ? tool : null;
    }

    private McpConfiguration LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            throw new FileNotFoundException("MCP config file not found.", _configPath);
        }

        var json = File.ReadAllText(_configPath);
        var config = JsonSerializer.Deserialize<McpConfiguration>(json, _jsonOptions);
        if (config is null || config.Mcp is null || config.Mcp.Tools is null || config.SchemaVersions is null)
        {
            throw new InvalidOperationException($"MCP config file '{_configPath}' is invalid.");
        }

        return config;
    }
}
