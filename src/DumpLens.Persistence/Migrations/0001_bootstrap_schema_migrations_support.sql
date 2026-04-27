CREATE TABLE IF NOT EXISTS migration_bootstrap (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    created_at_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO migration_bootstrap(id, created_at_utc)
VALUES (1, CURRENT_TIMESTAMP);
