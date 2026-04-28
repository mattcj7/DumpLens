CREATE TABLE IF NOT EXISTS schema_migrations (
    version TEXT NOT NULL PRIMARY KEY,
    name TEXT NOT NULL,
    applied_at_utc TEXT NOT NULL,
    checksum TEXT NOT NULL
);
