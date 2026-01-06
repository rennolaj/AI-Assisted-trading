using System;
using System.Collections.Generic;
using Mvp.Trading.Contracts;
using Xunit;

namespace Mvp.Trading.Risk.Tests;

public sealed class TradePlanBuilderTests
{
    [Fact]
    public void BuildPlan_IsDeterministic()
    {
        var builder = CreateBuilder(
            account: new AccountState("demo", "USD", 100000m, 0m, null),
            instrument: DefaultInstrument());

        var context = CreateContext();

        var first = builder.BuildPlan(context);
        var second = builder.BuildPlan(context);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Equal(first.Value?.PlanId, second.Value?.PlanId);
        Assert.Equal(first.Value?.Quantity, second.Value?.Quantity);
        Assert.Equal(3, first.Value?.TakeProfitTargets.Count);
        Assert.Equal(first.Value?.Quantity, SumTargets(first.Value?.TakeProfitTargets));
    }

    [Fact]
    public void BuildPlan_Rejects_WhenDailyRiskExceeded()
    {
        var builder = CreateBuilder(
            account: new AccountState("demo", "USD", 100000m, 6000m, null),
            instrument: DefaultInstrument());

        var context = CreateContext();

        var result = builder.BuildPlan(context);

        Assert.False(result.Ok);
        Assert.Equal("RISK_DAILY_BUDGET", result.Error?.Code);
    }

    [Fact]
    public void BuildPlan_Rejects_WhenStopDistanceZero()
    {
        var builder = CreateBuilder(
            account: new AccountState("demo", "USD", 100000m, 0m, null),
            instrument: DefaultInstrument());

        var context = CreateContext(stopLoss: 100m, close: 100m);

        var result = builder.BuildPlan(context);

        Assert.False(result.Ok);
        Assert.Equal("RISK_STOP_DISTANCE_ZERO", result.Error?.Code);
    }

    [Fact]
    public void BuildPlan_AppliesContractMultiplier()
    {
        var instrument = new InstrumentSpec("BTCUSD.P", 0.5m, 1m, 1m, 10m, 5m, 2m);
        var builder = CreateBuilder(
            account: new AccountState("demo", "USD", 100000m, 0m, null),
            instrument: instrument);

        var context = CreateContext(close: 100m, stopLoss: 90m);

        var result = builder.BuildPlan(context);

        Assert.True(result.Ok);
        Assert.Equal(50m, result.Value?.Quantity);
        Assert.Equal(1000m, result.Value?.PlannedRiskAmount);
        Assert.Equal(result.Value?.Quantity, SumTargets(result.Value?.TakeProfitTargets));
    }

    private static TradePlanBuilder CreateBuilder(AccountState account, InstrumentSpec instrument)
    {
        return new TradePlanBuilder(
            new StubAccountStateProvider(account),
            new StubInstrumentSpecProvider(instrument),
            new StubExecutionSettingsProvider(new ExecutionSettings("SIMULATED", 0.0m, 10, 30, 1)));
    }

    private static TradePlanContext CreateContext(decimal? close = 100m, decimal? stopLoss = 90m)
    {
        var alert = new AlertEvent(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "test",
            "idempo",
            new TradingViewFields("BTCUSD.P", "krakenfutures", "1", close, null),
            new IntentFields("LONG", "BTCUSD.P", "test"),
            "{}");

        var snapshot = new SignalSnapshot(
            "BTCUSD.P",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Array.Empty<TimeframeSnapshot>());

        var candidate = new ElliottCandidate(
            "cand-1",
            "IMPULSE",
            "W3",
            70m,
            0.7m,
            Array.Empty<RuleViolation>(),
            new InvalidationLevels(stopLoss, 110m));

        var candidates = new ElliottCandidates(Timeframe.M1, new[] { candidate });

        var decision = new LlmDecision(
            "ALLOWLONGW3",
            0.9m,
            "cand-1",
            "WAVEINVALIDATION",
            "ok");

        var policy = new RiskPolicy(1m, 5m, 5m, 50000m, "LONG,SHORT");

        return new TradePlanContext(
            alert,
            snapshot,
            candidates,
            decision,
            policy,
            "1.0",
            "1.0",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static InstrumentSpec DefaultInstrument()
    {
        return new InstrumentSpec("BTCUSD.P", 0.5m, 1m, 1m, 10m, 5m, 1m);
    }

    private static decimal SumTargets(IReadOnlyList<TakeProfitTarget>? targets)
    {
        if (targets is null)
        {
            return 0m;
        }

        decimal sum = 0m;
        foreach (var target in targets)
        {
            sum += target.Quantity;
        }

        return sum;
    }

    private sealed class StubAccountStateProvider : IAccountStateProvider
    {
        private readonly AccountState _state;

        public StubAccountStateProvider(AccountState state)
        {
            _state = state;
        }

        public AccountState GetAccountState() => _state;
    }

    private sealed class StubInstrumentSpecProvider : IInstrumentSpecProvider
    {
        private readonly InstrumentSpec _spec;

        public StubInstrumentSpecProvider(InstrumentSpec spec)
        {
            _spec = spec;
        }

        public InstrumentSpec? GetSpec(string symbol) => _spec;
    }

    private sealed class StubExecutionSettingsProvider : IExecutionSettingsProvider
    {
        private readonly ExecutionSettings _settings;

        public StubExecutionSettingsProvider(ExecutionSettings settings)
        {
            _settings = settings;
        }

        public ExecutionSettings GetSettings() => _settings;
    }
}
