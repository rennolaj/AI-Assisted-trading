using System.Text.Json;

namespace Mvp.Trading.Risk;

/// <summary>
/// Loads account state from config/account.json.
/// </summary>
public sealed class FileAccountStateProvider : IAccountStateProvider
{
    private const string ConfigFileName = "account.json";
    private readonly string _configPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lazy<AccountState> _state;

    public FileAccountStateProvider()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "config", ConfigFileName);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _state = new Lazy<AccountState>(LoadState);
    }

    public AccountState GetAccountState() => _state.Value;

    private AccountState LoadState()
    {
        if (!File.Exists(_configPath))
        {
            throw new FileNotFoundException("Account config file not found.", _configPath);
        }

        var json = File.ReadAllText(_configPath);
        var state = JsonSerializer.Deserialize<AccountState>(json, _jsonOptions);
        if (state is null)
        {
            throw new InvalidOperationException($"Account config file '{_configPath}' is invalid.");
        }

        return state;
    }
}
