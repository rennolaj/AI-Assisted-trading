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
