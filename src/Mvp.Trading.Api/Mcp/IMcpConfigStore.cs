namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Provides MCP tool configuration and schema versions from file-based sources.
/// </summary>
public interface IMcpConfigStore
{
    McpConfiguration GetConfig();

    McpToolConfig? GetToolConfig(string toolName);
}
