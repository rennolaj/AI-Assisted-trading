using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mvp.Trading.Contracts.Contracts;
using Npgsql;

namespace Mvp.Trading.Execution;

/// <summary>
/// PostgreSQL implementation of LLM adjudication store.
/// </summary>
public sealed class PostgresLlmAdjudicationStore : ILlmAdjudicationStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresLlmAdjudicationStore> _logger;

    public PostgresLlmAdjudicationStore(
        NpgsqlDataSource dataSource,
        ILogger<PostgresLlmAdjudicationStore> logger)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(LlmAdjudication adjudication, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO llm_adjudications (
                adjudication_id, alert_id, correlation_id,
                prompt_text, prompt_tokens,
                raw_response, completion_tokens, total_tokens,
                decision, reasoning, confidence,
                llm_provider, llm_model, response_time_ms, adjudicated_at_utc,
                parse_error, validation_errors
            ) VALUES (
                @adjudication_id, @alert_id, @correlation_id,
                @prompt_text, @prompt_tokens,
                @raw_response, @completion_tokens, @total_tokens,
                @decision, @reasoning, @confidence,
                @llm_provider, @llm_model, @response_time_ms, @adjudicated_at_utc,
                @parse_error, @validation_errors::jsonb
            )";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("adjudication_id", adjudication.AdjudicationId);
        cmd.Parameters.AddWithValue("alert_id", adjudication.AlertId);
        cmd.Parameters.AddWithValue("correlation_id", adjudication.CorrelationId);
        cmd.Parameters.AddWithValue("prompt_text", adjudication.PromptText);
        cmd.Parameters.AddWithValue("prompt_tokens", (object?)adjudication.PromptTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("raw_response", adjudication.RawResponse);
        cmd.Parameters.AddWithValue("completion_tokens", (object?)adjudication.CompletionTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("total_tokens", (object?)adjudication.TotalTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("decision", adjudication.Decision);
        cmd.Parameters.AddWithValue("reasoning", adjudication.Reasoning);
        cmd.Parameters.AddWithValue("confidence", (object?)adjudication.Confidence ?? DBNull.Value);
        cmd.Parameters.AddWithValue("llm_provider", adjudication.LlmProvider);
        cmd.Parameters.AddWithValue("llm_model", (object?)adjudication.LlmModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("response_time_ms", (object?)adjudication.ResponseTimeMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("adjudicated_at_utc", adjudication.AdjudicatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("parse_error", (object?)adjudication.ParseError ?? DBNull.Value);
        
        // Handle validation_errors as JSONB - must be valid JSON or NULL
        if (string.IsNullOrWhiteSpace(adjudication.ValidationErrors))
        {
            cmd.Parameters.AddWithValue("validation_errors", DBNull.Value);
        }
        else
        {
            cmd.Parameters.AddWithValue("validation_errors", NpgsqlTypes.NpgsqlDbType.Jsonb, adjudication.ValidationErrors);
        }

        await cmd.ExecuteNonQueryAsync(ct);
        
        _logger.LogInformation(
            "Saved LLM adjudication {AdjudicationId} for alert {AlertId}: {Decision} (provider: {Provider}, duration: {Duration}ms)",
            adjudication.AdjudicationId, 
            adjudication.AlertId, 
            adjudication.Decision,
            adjudication.LlmProvider,
            adjudication.ResponseTimeMs);
    }

    public async Task<LlmAdjudication?> GetByAlertIdAsync(Guid alertId, CancellationToken ct)
    {
        const string sql = @"
            SELECT 
                adjudication_id,
                alert_id,
                correlation_id,
                prompt_text,
                prompt_tokens,
                raw_response,
                completion_tokens,
                total_tokens,
                decision,
                reasoning,
                confidence,
                llm_provider,
                llm_model,
                response_time_ms,
                adjudicated_at_utc,
                parse_error,
                validation_errors
            FROM llm_adjudications 
            WHERE alert_id = @alert_id 
            ORDER BY adjudicated_at_utc DESC 
            LIMIT 1";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("alert_id", alertId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new LlmAdjudication
        {
            AdjudicationId = reader.GetGuid(0),
            AlertId = reader.GetGuid(1),
            CorrelationId = reader.GetGuid(2),
            PromptText = reader.GetString(3),
            PromptTokens = await reader.IsDBNullAsync(4, ct) ? null : reader.GetInt32(4),
            RawResponse = reader.GetString(5),
            CompletionTokens = await reader.IsDBNullAsync(6, ct) ? null : reader.GetInt32(6),
            TotalTokens = await reader.IsDBNullAsync(7, ct) ? null : reader.GetInt32(7),
            Decision = reader.GetString(8),
            Reasoning = reader.GetString(9),
            Confidence = await reader.IsDBNullAsync(10, ct) ? null : reader.GetDecimal(10),
            LlmProvider = reader.GetString(11),
            LlmModel = await reader.IsDBNullAsync(12, ct) ? null : reader.GetString(12),
            ResponseTimeMs = await reader.IsDBNullAsync(13, ct) ? null : reader.GetInt32(13),
            AdjudicatedAtUtc = await reader.GetFieldValueAsync<DateTimeOffset>(14, ct),
            ParseError = await reader.IsDBNullAsync(15, ct) ? null : reader.GetString(15),
            ValidationErrors = await reader.IsDBNullAsync(16, ct) ? null : reader.GetString(16)
        };
    }
}
