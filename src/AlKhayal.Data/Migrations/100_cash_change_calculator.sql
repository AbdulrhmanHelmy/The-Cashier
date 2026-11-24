ALTER TABLE ApplicationSettings ADD COLUMN ShowCashChangeCalculator INTEGER NOT NULL DEFAULT 1
    CHECK (ShowCashChangeCalculator IN (0, 1));
