# Event Contracts

Event payloads are part of the public contract (future migrations must respect them).

## Envelope
Fields:
- event_id (GUID)
- stream_id (GUID)
- event_type (string)
- occurred_at (ISO-8601)
- effective_date (YYYY-MM-DD)
- actor_user_id (GUID)
- device_id (GUID)
- correlation_id (GUID)
- causation_event_id (GUID? nullable)
- payload_schema_version (int)
- payload_json (string)
- prev_hash (string? optional)
- hash (string)

Rules:
- Ordering: effective_date ASC, occurred_at ASC
- Hash chain: hash = H(prev_hash + canonical(envelope))

## Domain Event Types
Document each event type and its payload schema version here as the project grows.
This file is updated whenever a payload changes.
