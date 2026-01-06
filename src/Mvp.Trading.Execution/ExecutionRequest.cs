using System;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Execution;

/// <summary>
/// Input for execution requests.
/// </summary>
public sealed record ExecutionRequest(Guid AlertId, TradePlan Plan);
