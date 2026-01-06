namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Provides prompt templates for MCP tool invocations.
/// </summary>
public interface IPromptTemplateStore
{
    string RenderAdjudicateElliottPrompt(string input);

    string RenderExplainStopLossPrompt(string input);
}
