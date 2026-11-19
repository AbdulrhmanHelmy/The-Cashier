-- Optional explicit selling price per product unit. NULL preserves the legacy
-- behavior of deriving a selected-unit price from the variant's base price.
ALTER TABLE ProductUnits ADD COLUMN SellPriceScaled INTEGER NULL CHECK(SellPriceScaled IS NULL OR SellPriceScaled >= 0);
