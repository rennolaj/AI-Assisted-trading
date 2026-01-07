create table if not exists idempotency_keys (
    idempotency_key text primary key,
    first_seen_utc timestamptz not null
);

create table if not exists alerts (
    alert_id uuid primary key,
    idempotency_key text not null references idempotency_keys(idempotency_key),
    received_at_utc timestamptz not null,
    source text not null,
    raw_payload text not null,
    alert_json jsonb not null
);

create index if not exists alerts_received_at_idx on alerts (received_at_utc desc);
create unique index if not exists alerts_idempotency_key_idx on alerts (idempotency_key);

create table if not exists alert_processing (
    alert_id uuid primary key references alerts(alert_id),
    idempotency_key text not null,
    status text not null,
    last_updated_utc timestamptz not null,
    error_message text
);

create index if not exists alert_processing_status_idx on alert_processing (status);

create table if not exists open_trades (
    trade_id uuid primary key,
    exchange_id text not null,
    symbol text not null,
    side text not null,
    entry_price numeric,
    invalidation_price numeric,
    status text not null,
    opened_at_utc timestamptz not null,
    last_checked_utc timestamptz,
    last_price numeric,
    invalidated_at_utc timestamptz,
    invalidation_reason text
);

create index if not exists open_trades_status_idx on open_trades (status);
create index if not exists open_trades_exchange_idx on open_trades (exchange_id);

create table if not exists indicator_snapshots (
    alert_id uuid primary key references alerts(alert_id),
    correlation_id uuid not null,
    computed_at_utc timestamptz not null,
    evaluation_time_utc timestamptz not null,
    symbol text not null,
    mode text not null,
    direction text not null,
    snapshot_json jsonb not null
);

create index if not exists indicator_snapshots_symbol_idx on indicator_snapshots (symbol);

create table if not exists elliott_candidates (
    alert_id uuid primary key references alerts(alert_id),
    computed_at_utc timestamptz not null,
    evaluation_time_utc timestamptz not null,
    symbol text not null,
    base_timeframe text not null,
    parameters_json jsonb not null,
    candidates_json jsonb not null
);

create index if not exists elliott_candidates_symbol_idx on elliott_candidates (symbol);

create table if not exists trade_plan (
    plan_id uuid primary key,
    alert_id uuid references alerts(alert_id),
    created_at_utc timestamptz not null,
    plan_json jsonb not null
);

create index if not exists trade_plan_alert_idx on trade_plan (alert_id);

create table if not exists execution_intent (
    execution_id uuid primary key,
    plan_id uuid not null references trade_plan(plan_id),
    mode text not null,
    status text not null,
    created_at_utc timestamptz not null
);

create index if not exists execution_intent_plan_idx on execution_intent (plan_id);

create table if not exists order_receipt (
    receipt_id uuid primary key,
    execution_id uuid not null references execution_intent(execution_id),
    order_kind text not null,
    client_order_id text not null,
    exchange_order_id text,
    status text not null,
    qty numeric,
    price numeric,
    created_at_utc timestamptz not null
);

create index if not exists order_receipt_execution_idx on order_receipt (execution_id);

create table if not exists fill_receipt (
    receipt_id uuid primary key,
    execution_id uuid not null references execution_intent(execution_id),
    exchange_order_id text,
    fill_qty numeric not null,
    fill_price numeric not null,
    fee_amount numeric,
    created_at_utc timestamptz not null
);

create index if not exists fill_receipt_execution_idx on fill_receipt (execution_id);

create table if not exists reconciliation_state (
    execution_id uuid primary key references execution_intent(execution_id),
    status text not null,
    details text,
    last_checked_utc timestamptz not null,
    reconciliation_type text not null default 'OPEN_ORDERS',
    discrepancy_count int not null default 0,
    last_error text
);

create table if not exists reconciliation_discrepancy (
    discrepancy_id uuid primary key default gen_random_uuid(),
    execution_id uuid references execution_intent(execution_id),
    detected_at_utc timestamptz not null default now(),
    discrepancy_type text not null,
    internal_state jsonb not null,
    exchange_state jsonb not null,
    details text,
    resolved boolean not null default false,
    resolved_at_utc timestamptz,
    resolution_notes text
);

create index if not exists idx_reconciliation_discrepancy_execution on reconciliation_discrepancy(execution_id);
create index if not exists idx_reconciliation_discrepancy_detected on reconciliation_discrepancy(detected_at_utc);
create index if not exists idx_reconciliation_discrepancy_unresolved on reconciliation_discrepancy(resolved) where resolved = false;
create index if not exists idx_reconciliation_discrepancy_type on reconciliation_discrepancy(discrepancy_type);

create table if not exists daily_risk (
    risk_date date primary key,
    equity numeric not null,
    planned_risk numeric not null,
    updated_at_utc timestamptz not null
);

create table if not exists execution_heartbeat (
    service_name text primary key,
    last_beat_utc timestamptz not null,
    stale_threshold_seconds int not null
);

-- System state table for kill switch and other global flags
create table if not exists system_state (
    key text primary key,
    value jsonb not null,
    updated_at_utc timestamptz not null default now(),
    updated_by text
);

-- Kill switch audit trail
create table if not exists kill_switch_audit (
    audit_id uuid primary key default gen_random_uuid(),
    action text not null, -- ACTIVATED, DEACTIVATED
    level text not null,  -- PAUSE_NEW, PAUSE_ALL, EMERGENCY_STOP
    reason text not null,
    activated_by text,
    timestamp_utc timestamptz not null default now()
);

create index if not exists idx_kill_switch_audit_timestamp on kill_switch_audit(timestamp_utc desc);

-- Initialize kill switch as inactive
insert into system_state (key, value, updated_by)
values ('kill_switch', '{"active": false, "level": "PAUSE_ALL", "reason": null, "activated_at": null}'::jsonb, 'system')
on conflict (key) do nothing;
