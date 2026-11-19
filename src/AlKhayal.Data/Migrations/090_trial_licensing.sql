CREATE TABLE IF NOT EXISTS TrialState (
    Singleton INTEGER PRIMARY KEY CHECK (Singleton = 1),
    TrialId TEXT NOT NULL,
    DeviceFingerprint TEXT NOT NULL,
    StartedAtUtc TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL,
    LastSeenUtc TEXT NOT NULL,
    HighestObservedUtc TEXT NOT NULL,
    Status TEXT NOT NULL,
    StateRevision INTEGER NOT NULL,
    IntegrityMetadata TEXT NOT NULL
);
