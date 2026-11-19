ALTER TABLE ProductDetails ADD COLUMN AllowSaleWhenOutOfStock INTEGER NOT NULL DEFAULT 1 CHECK(AllowSaleWhenOutOfStock IN (0,1));
