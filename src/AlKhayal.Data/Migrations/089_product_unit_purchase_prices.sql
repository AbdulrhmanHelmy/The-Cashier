-- Nullable prices preserve existing product units and their reference-price fallback.
ALTER TABLE ProductUnits ADD COLUMN UnitPurchasePriceScaled INTEGER NULL
CHECK(UnitPurchasePriceScaled IS NULL OR UnitPurchasePriceScaled >= 0);
