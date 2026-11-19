CREATE TABLE AlertReadStates (
    UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    AlertId TEXT NOT NULL,
    ReadUtc TEXT NOT NULL,
    PRIMARY KEY(UserId, AlertId)
);
CREATE INDEX IX_AlertReadStates_User ON AlertReadStates(UserId, ReadUtc);
