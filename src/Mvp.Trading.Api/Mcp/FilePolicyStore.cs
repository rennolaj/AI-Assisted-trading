using System.Text.Json;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Api.Mcp;

/// <summary>
/// Loads risk-policy.json from the application config directory.
/// </summary>
public sealed class FilePolicyStore : IPolicyStore
{
    private const string PolicyFileName = "risk-policy.json";
    private readonly string _policyPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lazy<RiskPolicy> _policy;

    public FilePolicyStore()
    {
        _policyPath = Path.Combine(AppContext.BaseDirectory, "config", PolicyFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _policy = new Lazy<RiskPolicy>(LoadPolicy);
    }

    public RiskPolicy GetRiskPolicy() => _policy.Value;

    private RiskPolicy LoadPolicy()
    {
        if (!File.Exists(_policyPath))
        {
            throw new FileNotFoundException("Risk policy file not found.", _policyPath);
        }

        var json = File.ReadAllText(_policyPath);
        var policy = JsonSerializer.Deserialize<RiskPolicy>(json, _jsonOptions);
        if (policy is null)
        {
            throw new InvalidOperationException($"Risk policy file '{_policyPath}' is invalid.");
        }

        return policy;
    }
}
