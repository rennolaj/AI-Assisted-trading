using System;

namespace Mvp.Trading.Api.Services;

/// <summary>
/// Represents the latest processing status for an alert.
/// </summary>
public sealed record AlertProcessingStatus(
    Guid AlertId,
    string IdempotencyKey,
    string Status,
    DateTimeOffset LastUpdatedUtc,
    string? ErrorMessage
);
