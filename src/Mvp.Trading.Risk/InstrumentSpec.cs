namespace Mvp.Trading.Risk;

/// <summary>
/// Exchange-specific trading constraints for a symbol.
/// </summary>
public sealed record InstrumentSpec(
    string Symbol,
    decimal PriceTick,
    decimal QtyStep,
    decimal MinQty,
    decimal MinNotional,
    decimal MaxLeverage,
    decimal ContractMultiplier
);

/// <summary>
/// Container for instrument specs loaded from config.
/// </summary>
public sealed record InstrumentSpecCatalog(
    InstrumentSpec[] Instruments
);
