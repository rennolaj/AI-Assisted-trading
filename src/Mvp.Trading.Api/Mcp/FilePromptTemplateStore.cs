using System.Collections.Concurrent;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Loads prompt templates from the prompts folder and renders inputs into them.
/// </summary>
public sealed class FilePromptTemplateStore : IPromptTemplateStore
{
    private const string AdjudicateTemplateFile = "adjudicate-elliott.md";
    private const string ExplainStopLossTemplateFile = "explain-stoploss.md";
    private const string InputToken = "{{input}}";
    private readonly string _promptRoot;
    private readonly ConcurrentDictionary<string, string> _templates = new(StringComparer.OrdinalIgnoreCase);

    public FilePromptTemplateStore()
    {
        _promptRoot = Path.Combine(AppContext.BaseDirectory, "prompts");
    }

    public string RenderAdjudicateElliottPrompt(string input)
    {
        return RenderTemplate(AdjudicateTemplateFile, input);
    }

    public string RenderExplainStopLossPrompt(string input)
    {
        return RenderTemplate(ExplainStopLossTemplateFile, input);
    }

    private string RenderTemplate(string fileName, string input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var template = _templates.GetOrAdd(fileName, LoadTemplate);
        return template.Replace(InputToken, input);
    }

    private string LoadTemplate(string fileName)
    {
        var path = Path.Combine(_promptRoot, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Prompt template '{fileName}' not found.", path);
        }

        var template = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException($"Prompt template '{fileName}' is empty.");
        }

        return template;
    }
}
