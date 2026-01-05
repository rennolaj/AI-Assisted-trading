namespace Mvp.Trading.Api.Services;

/// <summary>
/// Configuration for Postgres-backed storage.
/// </summary>
public sealed class PostgresOptions
{
    /// <summary>
    /// Connection string used by the Postgres data source.
    /// </summary>
    public string ConnectionString { get; init; } = string.Empty;
}
