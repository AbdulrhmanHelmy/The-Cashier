CREATE TABLE ScaleBarcodeProfiles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL CHECK(length(Name) BETWEEN 1 AND 80),
    Prefix TEXT NOT NULL UNIQUE CHECK(length(Prefix) BETWEEN 1 AND 6 AND Prefix NOT GLOB '*[^0-9]*'),
    ProductLength INTEGER NOT NULL CHECK(ProductLength BETWEEN 1 AND 8),
    ValueType TEXT NOT NULL CHECK(ValueType IN ('weight','price')),
    ValueLength INTEGER NOT NULL CHECK(ValueLength BETWEEN 1 AND 8),
    ValueDecimals INTEGER NOT NULL CHECK(ValueDecimals BETWEEN 0 AND 8),
    ValueHasCheckDigit INTEGER NOT NULL DEFAULT 0 CHECK(ValueHasCheckDigit IN (0,1)),
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)),
    Version INTEGER NOT NULL DEFAULT 1 CHECK(Version > 0),
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    CHECK(length(Prefix) + ProductLength + ValueLength = 12),
    CHECK(ValueDecimals <= ValueLength - ValueHasCheckDigit)
);
