namespace Mvp.Trading.Contracts.Telemetry;

/// <summary>
/// Service for recording application metrics using OpenTelemetry.
/// </summary>
public interface IMetricsService
{
    // Alert Processing Metrics
    void RecordAlertReceived(string exchange, string symbol);
    void RecordAlertProcessed(string outcome); // "accepted", "rejected_indicator", "rejected_elliott", "rejected_llm", "error"
    void RecordAlertProcessingDuration(TimeSpan duration);

    // Execution Metrics
    void RecordExecutionOutcome(string outcome); // "filled", "placed", "rejected_risk", "rejected_heartbeat", "error"
    void RecordExecutionDuration(TimeSpan duration, string stage); // stage: "total", "llm_adjudication", "order_placement"
    void RecordOrderPlaced(string direction, string orderType); // direction: "LONG"/"SHORT", type: "LIMIT"/"MARKET"

    // Order Management Metrics
    void RecordOrderCancelled(string reason);
    void RecordOrderFilled(string symbol, string direction, decimal quantity, decimal price);
    void RecordStopLossTriggered(string symbol);
    void RecordTakeProfitHit(string symbol, int targetNumber);

    // Error Metrics
    void RecordError(string component, string errorType); // component: "AlertWorker", "ExecutionService", etc.
    void RecordApiError(string provider, string endpoint, string errorCode);

    // System Metrics (Gauges)
    void SetActiveTradesGauge(int count);
    void SetQueueDepthGauge(int count);
    void SetReconciliationDiscrepanciesGauge(int count);
}
