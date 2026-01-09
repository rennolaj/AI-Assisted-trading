-- Migration 007: Create llm_adjudications table
-- Purpose: Store complete LLM adjudication interactions for debugging and analysis
-- Date: 2026-01-09

-- Create llm_adjudications table
CREATE TABLE llm_adjudications (
    adjudication_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_id UUID NOT NULL REFERENCES alerts(alert_id) ON DELETE CASCADE,
    correlation_id UUID NOT NULL,
    
    -- LLM Request
    prompt_text TEXT NOT NULL,
    prompt_tokens INTEGER,
    
    -- LLM Response
    raw_response TEXT NOT NULL,
    completion_tokens INTEGER,
    total_tokens INTEGER,
    
    -- Parsed Decision
    decision VARCHAR(50) NOT NULL,
    reasoning TEXT NOT NULL,
    confidence DECIMAL(5,2),
    
    -- Metadata
    llm_provider VARCHAR(50) NOT NULL,
    llm_model VARCHAR(100),
    response_time_ms INTEGER,
    adjudicated_at_utc TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    
    -- Error tracking
    parse_error TEXT,
    validation_errors JSONB
);

-- Indexes for query performance
CREATE INDEX idx_llm_adjudications_alert ON llm_adjudications(alert_id);
CREATE INDEX idx_llm_adjudications_decision ON llm_adjudications(decision);
CREATE INDEX idx_llm_adjudications_time ON llm_adjudications(adjudicated_at_utc DESC);
CREATE INDEX idx_llm_adjudications_provider ON llm_adjudications(llm_provider);
CREATE INDEX idx_llm_adjudications_correlation ON llm_adjudications(correlation_id);

-- Add comments for documentation
COMMENT ON TABLE llm_adjudications IS 'Stores complete LLM adjudication interactions for debugging and analysis';
COMMENT ON COLUMN llm_adjudications.prompt_text IS 'Full prompt sent to LLM (includes system + user messages)';
COMMENT ON COLUMN llm_adjudications.raw_response IS 'Raw LLM response before JSON parsing';
COMMENT ON COLUMN llm_adjudications.reasoning IS 'Extracted reasoning from parsed decision';
COMMENT ON COLUMN llm_adjudications.parse_error IS 'Error message if JSON parsing failed';
COMMENT ON COLUMN llm_adjudications.validation_errors IS 'Schema validation errors as JSON array';
