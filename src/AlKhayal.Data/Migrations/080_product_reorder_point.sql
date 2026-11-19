ALTER TABLE ProductDetails ADD COLUMN ReorderPointScaled INTEGER NOT NULL DEFAULT 0 CHECK (ReorderPointScaled >= 0);
CREATE INDEX IX_InventoryBalances_VariantQuantity ON InventoryBalances(VariantId, QuantityBaseScaled);
CREATE INDEX IX_ProductDetails_ReorderCandidates ON ProductDetails(ProductId, ReorderPointScaled) WHERE ReorderPointScaled > 0 AND ProductType = 'stock' AND IsActive = 1 AND ArchivedUtc IS NULL;
