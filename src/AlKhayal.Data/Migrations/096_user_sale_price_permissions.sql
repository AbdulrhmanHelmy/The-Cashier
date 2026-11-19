-- Manual selling-price changes are disabled unless explicitly granted.
CREATE TABLE UserSalePricePermissions (
    UserId INTEGER PRIMARY KEY REFERENCES Users(Id) ON DELETE CASCADE,
    CanEdit INTEGER NOT NULL DEFAULT 0 CHECK (CanEdit IN (0,1)),
    GrantedUtc TEXT NOT NULL,
    ChangedByUserId INTEGER NOT NULL REFERENCES Users(Id)
);
