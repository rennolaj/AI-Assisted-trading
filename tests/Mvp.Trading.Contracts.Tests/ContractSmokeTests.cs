using Xunit;

namespace Mvp.Trading.Contracts.Tests;

/// <summary>
/// Basic smoke tests for the contracts assembly.
/// </summary>
public sealed class ContractSmokeTests
{
    [Fact]
    public void ContractsAssemblyLoads()
    {
        var assembly = typeof(Mvp.Trading.Contracts.AlertEvent).Assembly;
        Assert.NotNull(assembly.FullName);
    }
}
