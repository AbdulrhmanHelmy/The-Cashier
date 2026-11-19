-- POS card stock visibility is disabled unless explicitly granted.
CREATE TABLE UserPosStockPermissions (
    UserId INTEGER PRIMARY KEY REFERENCES Users(Id) ON DELETE CASCADE,
    GrantedUtc TEXT NOT NULL,
    ChangedByUserId INTEGER NOT NULL REFERENCES Users(Id)
);
