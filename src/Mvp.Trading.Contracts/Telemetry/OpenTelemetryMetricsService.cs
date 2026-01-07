using System.Diagnostics.Metrics;

namespace Mvp.Trading.Contracts.Telemetry;

/// <summary>
/// OpenTelemetry-based implementation of metrics service.
/// </summary>
public sealed class OpenTelemetryMetricsService : IMetricsService
{
    private readonly Meter _meter;
    
    // Counters
    private readonly Counter<long> _alertsReceived;
    private readonly Counter<long> _alertsProcessed;
    private readonly Counter<long> _executionsProcessed;
    private readonly Counter<long> _ordersPlaced;
    private readonly Counter<long> _ordersCancelled;
    private readonly Counter<long> _ordersFilled;
    private readonly Counter<long> _stopLossTriggered;
    private readonly Counter<long> _takeProfitHit;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _apiErrors;
    
    // Histograms
    private readonly Histogram<double> _alertProcessingDuration;
    private readonly Histogram<double> _executionDuration;
    
    // Gauges (backing fields for ObservableGauge callbacks)
    private int _activeTrades;
    private int _queueDepth;
    private int _reconciliationDiscrepancies;

    public OpenTelemetryMetricsService()
    {
        _meter = new Meter("Mvp.Trading", "1.0.0");

        // Initialize counters
        _alertsReceived = _meter.CreateCounter<long>(
            "alerts_received_total",
            unit: "alerts",
            description: "Total alerts received");
        
        _alertsProcessed = _meter.CreateCounter<long>(
            "alerts_processed_total",
            unit: "alerts",
            description: "Total alerts processed");
        
        _executionsProcessed = _meter.CreateCounter<long>(
            "executions_processed_total",
            unit: "executions",
            description: "Total executions processed");
        
        _ordersPlaced = _meter.CreateCounter<long>(
            "orders_placed_total",
            unit: "orders",
            description: "Total orders placed");
        
        _ordersCancelled = _meter.CreateCounter<long>(
            "orders_cancelled_total",
            unit: "orders",
            description: "Total orders cancelled");
        
        _ordersFilled = _meter.CreateCounter<long>(
            "orders_filled_total",
            unit: "orders",
            description: "Total orders filled");
        
        _stopLossTriggered = _meter.CreateCounter<long>(
            "stop_loss_triggered_total",
            unit: "events",
            description: "Total stop-loss triggers");
        
        _takeProfitHit = _meter.CreateCounter<long>(
            "take_profit_hit_total",
            unit: "events",
            description: "Total take-profit hits");
        
        _errors = _meter.CreateCounter<long>(
            "errors_total",
            unit: "errors",
            description: "Total errors");
        
        _apiErrors = _meter.CreateCounter<long>(
            "api_errors_total",
            unit: "errors",
            description: "Total API errors");

        // Initialize histograms
        _alertProcessingDuration = _meter.CreateHistogram<double>(
            "alert_processing_duration_seconds",
            unit: "seconds",
            description: "Alert processing duration");
        
        _executionDuration = _meter.CreateHistogram<double>(
            "execution_duration_seconds",
            unit: "seconds",
            description: "Execution duration");

        // Initialize gauges with observable callbacks
        _meter.CreateObservableGauge(
            "active_trades",
            () => _activeTrades,
            unit: "trades",
            description: "Current active trades");
        
        _meter.CreateObservableGauge(
            "queue_depth",
            () => _queueDepth,
            unit: "alerts",
            description: "Current queue depth");
        
        _meter.CreateObservableGauge(
            "reconciliation_discrepancies",
            () => _reconciliationDiscrepancies,
            unit: "discrepancies",
            description: "Current unresolved discrepancies");
    }

    public void RecordAlertReceived(string exchange, string symbol)
    {
        _alertsReceived.Add(1,
            new KeyValuePair<string, object?>("exchange", exchange),
            new KeyValuePair<string, object?>("symbol", symbol));
    }

    public void RecordAlertProcessed(string outcome)
    {
        _alertsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordAlertProcessingDuration(TimeSpan duration)
    {
        _alertProcessingDuration.Record(duration.TotalSeconds);
    }

    public void RecordExecutionOutcome(string outcome)
    {
        _executionsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordExecutionDuration(TimeSpan duration, string stage)
    {
        _executionDuration.Record(duration.TotalSeconds,
            new KeyValuePair<string, object?>("stage", stage));
    }

    public void RecordOrderPlaced(string direction, string orderType)
    {
        _ordersPlaced.Add(1,
            new KeyValuePair<string, object?>("direction", direction),
            new KeyValuePair<string, object?>("order_type", orderType));
    }

    public void RecordOrderCancelled(string reason)
    {
        _ordersCancelled.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordOrderFilled(string symbol, string direction, decimal quantity, decimal price)
    {
        _ordersFilled.Add(1,
            new KeyValuePair<string, object?>("symbol", symbol),
            new KeyValuePair<string, object?>("direction", direction));
    }

    public void RecordStopLossTriggered(string symbol)
    {
        _stopLossTriggered.Add(1, new KeyValuePair<string, object?>("symbol", symbol));
    }

    public void RecordTakeProfitHit(string symbol, int targetNumber)
    {
        _takeProfitHit.Add(1,
            new KeyValuePair<string, object?>("symbol", symbol),
            new KeyValuePair<string, object?>("target", targetNumber));
    }

    public void RecordError(string component, string errorType)
    {
        _errors.Add(1,
            new KeyValuePair<string, object?>("component", component),
            new KeyValuePair<string, object?>("error_type", errorType));
    }

    public void RecordApiError(string provider, string endpoint, string errorCode)
    {
        _apiErrors.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("error_code", errorCode));
    }

    public void SetActiveTradesGauge(int count)
    {
        _activeTrades = count;
    }

    public void SetQueueDepthGauge(int count)
    {
        _queueDepth = count;
    }

    public void SetReconciliationDiscrepanciesGauge(int count)
    {
        _reconciliationDiscrepancies = count;
    }
}
