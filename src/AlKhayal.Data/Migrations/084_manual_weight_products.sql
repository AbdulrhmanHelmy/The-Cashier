-- Existing products remain piece based. Quantities and prices are unchanged.
ALTER TABLE ProductDetails ADD COLUMN SellingType TEXT NOT NULL DEFAULT 'piece'
    CHECK (SellingType IN ('piece', 'weight'));

