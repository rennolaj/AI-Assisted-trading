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

create table if not exists alert_processing (
    alert_id uuid primary key references alerts(alert_id),
    idempotency_key text not null,
    status text not null,
    last_updated_utc timestamptz not null,
    error_message text
);

create index if not exists alert_processing_status_idx on alert_processing (status);
