using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class CatalogVariantPersistenceTests
    {
        [Fact]
        public async Task Audit_log_triggers_abort_on_update_and_delete()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "audit-user");

                long auditId;
                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    auditId = connection.ExecuteScalar<long>(
                        "INSERT INTO CatalogAuditLog (ProductId, EntityType, EntityId, Action, ChangeJson, ChangedByUserId, ChangedUtc) " +
                        "VALUES (NULL, 'attribute', 1, 'create', '{\"test\":\"value\"}', @UserId, @Now); SELECT last_insert_rowid();",
                        new { UserId = userId, Now = DateTime.UtcNow.ToString("o") });

                    var updateEx = Assert.Throws<SQLiteException>(() =>
                        connection.Execute("UPDATE CatalogAuditLog SET Action='hacked' WHERE Id=@Id", new { Id = auditId }));
                    Assert.Contains("catalog_audit_immutable", updateEx.Message);

                    var deleteEx = Assert.Throws<SQLiteException>(() =>
                        connection.Execute("DELETE FROM CatalogAuditLog WHERE Id=@Id", new { Id = auditId }));
                    Assert.Contains("catalog_audit_immutable", deleteEx.Message);
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Configured_variant_product_persists_and_survives_process_restart()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "var-admin");
                var store = new SqliteCatalogStore(cs);

                var boxUnit = await store.SaveUnitAsync(new SaveUnitCommand { Code = "BOX", Name = "علبة", Symbol = "ع", IsActive = true }, "BOX", CancellationToken.None);

                // Exercise generic variants using custom attributes.
                var gradeA = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 3, Value = "M", SortOrder = 1, IsActive = true, UserId = userId }, CancellationToken.None);
                var gradeB = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 3, Value = "L", SortOrder = 2, IsActive = true, UserId = userId }, CancellationToken.None);
                var originLocal = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 4, Value = "Red", SortOrder = 1, IsActive = true, UserId = userId }, CancellationToken.None);

                var createRecord = new CreateProductRecord
                {
                    OperationKey = "create-shirt",
                    RequestHash = "hash-shirt",
                    Name = "قميص",
                    Description = "قميص قطني",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 1500000,
                    PurchasePriceScaled = 1000000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = true,
                        CapabilityKeys = new List<string> { "product-variants", "multiple-units", "product-barcode-settings" },
                        Attributes = new List<ProductAttributeAssignmentConfigRecord>
                        {
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 3, ValueIds = new List<long> { gradeA.Id, gradeB.Id }, SortOrder = 1 },
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 4, ValueIds = new List<long> { originLocal.Id }, SortOrder = 2 }
                        },
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true },
                            new ProductUnitConfigRecord { UnitId = boxUnit.Id, ToBaseNumerator = 10, ToBaseDenominator = 1, IsBase = false, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                Name = "قميص — M / أحمر",
                                IsDefault = true,
                                IsActive = true,
                                AttributeValueIds = new List<long> { gradeA.Id, originLocal.Id },
                                Sku = "SHIRT-M-RED",
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "111222333444", BarcodeType = "gtin", IsPrimary = true }
                                }
                            },
                            new ProductVariantConfigRecord
                            {
                                Name = "قميص — L / أحمر",
                                IsDefault = false,
                                IsActive = true,
                                AttributeValueIds = new List<long> { gradeB.Id, originLocal.Id },
                                SellPriceOverrideScaled = 1600000,
                                Sku = "SHIRT-L-RED",
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "555666777888", BarcodeType = "gtin", IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var created = await store.CreateProductAsync(createRecord, CancellationToken.None);
                Assert.NotNull(created);
                Assert.Equal("قميص", created.Name);

                // Re-instantiate store to simulate process restart
                var restartedStore = new SqliteCatalogStore(cs);
                var detail = await restartedStore.GetProductDetailAsync(created.Id, CancellationToken.None);
                Assert.NotNull(detail);
                Assert.True(detail.VariantMode);
                Assert.Equal(2, detail.Units.Count);
                Assert.Equal(2, detail.Attributes.Count);
                Assert.Equal(2, detail.Variants.Count);

                var defaultVar = Assert.Single(detail.Variants, v => v.IsDefault);
                Assert.Equal("SHIRT-M-RED", defaultVar.Sku.Sku);
                Assert.Single(defaultVar.Barcodes);
                Assert.Equal("111222333444", defaultVar.Barcodes[0].Barcode);
                Assert.True(defaultVar.Barcodes[0].IsPrimary);

                var nonDefaultVar = Assert.Single(detail.Variants, v => !v.IsDefault);
                Assert.Equal("SHIRT-L-RED", nonDefaultVar.Sku.Sku);
                Assert.Equal(1600000L, nonDefaultVar.SellPriceOverrideScaled);

                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    var auditCount = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CatalogAuditLog WHERE ProductId=@ProductId", new { ProductId = created.Id });
                    Assert.True(auditCount >= 1);
                    Assert.Empty(connection.Query<string>("PRAGMA foreign_key_check"));
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Global_sku_and_barcode_uniqueness_includes_retired_rows()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "retire-admin");
                var store = new SqliteCatalogStore(cs);

                var create = new CreateProductRecord
                {
                    OperationKey = "create-p1",
                    RequestHash = "hash-p1",
                    Name = "منتج أول",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                IsDefault = true,
                                IsActive = true,
                                Sku = "UNIQUE-SKU-1",
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "BARCODE-1", BarcodeType = "internal", IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var p1 = await store.CreateProductAsync(create, CancellationToken.None);
                var detail1 = await store.GetProductDetailAsync(p1.Id, CancellationToken.None);
                var v1 = detail1.Variants[0];
                var b1 = v1.Barcodes[0];

                // Update product 1: retire the barcode and change the SKU
                var update = new UpdateProductRecord
                {
                    ProductId = p1.Id,
                    Name = "منتج أول معدل",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    Version = p1.Version,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { Id = detail1.Units[0].Id, UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                Id = v1.Id,
                                IsDefault = true,
                                IsActive = true,
                                Sku = "UNIQUE-SKU-NEW",
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Id = b1.Id, Barcode = b1.Barcode, BarcodeType = b1.BarcodeType, Retire = true }
                                }
                            }
                        }
                    }
                };

                await store.UpdateProductAsync(update, CancellationToken.None);

                // Now try creating product 2 with the retired SKU-1
                var dupSkuCreate = new CreateProductRecord
                {
                    OperationKey = "create-p2-sku",
                    RequestHash = "hash-p2-sku",
                    Name = "منتج ثان",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord { IsDefault = true, IsActive = true, Sku = "UNIQUE-SKU-1" }
                        }
                    }
                };
                var skuEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.CreateProductAsync(dupSkuCreate, CancellationToken.None));
                Assert.Equal("duplicate_sku", skuEx.Code);

                // Now try creating product 3 with the retired BARCODE-1
                var dupBcCreate = new CreateProductRecord
                {
                    OperationKey = "create-p3-bc",
                    RequestHash = "hash-p3-bc",
                    Name = "منتج ثالث",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                IsDefault = true,
                                IsActive = true,
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "BARCODE-1", BarcodeType = "internal" }
                                }
                            }
                        }
                    }
                };
                var bcEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.CreateProductAsync(dupBcCreate, CancellationToken.None));
                Assert.Equal("duplicate_barcode", bcEx.Code);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Barcode_lookup_returns_exact_identity_and_retired_status()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "lookup-admin");
                var store = new SqliteCatalogStore(cs);

                var create = new CreateProductRecord
                {
                    OperationKey = "create-scan-prod",
                    RequestHash = "hash-scan",
                    Name = "عصير",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 250000,
                    PurchasePriceScaled = 150000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                IsDefault = true,
                                IsActive = true,
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "SCAN-ACTIVE-01", BarcodeType = "gtin", IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var p = await store.CreateProductAsync(create, CancellationToken.None);

                // Active barcode lookup
                var found = await store.LookupBarcodeAsync("scan-active-01", CancellationToken.None);
                Assert.NotNull(found);
                Assert.Equal("SCAN-ACTIVE-01", found.Code);
                Assert.Equal("عصير", found.ProductName);
                Assert.False(found.IsBarcodeRetired);
                Assert.True(found.IsProductActive);
                Assert.Equal(250000L, found.EffectiveSalePriceScaled);

                // Unknown barcode lookup
                var notFound = await store.LookupBarcodeAsync("UNKNOWN-CODE", CancellationToken.None);
                Assert.Null(notFound);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Conversion_locks_when_barcode_references_unit()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "lock-admin");
                var store = new SqliteCatalogStore(cs);

                var packUnit = await store.SaveUnitAsync(new SaveUnitCommand { Code = "PACK", Name = "طرد", Symbol = "ط", IsActive = true }, "PACK", CancellationToken.None);

                var create = new CreateProductRecord
                {
                    OperationKey = "create-locked-unit",
                    RequestHash = "hash-locked-unit",
                    Name = "بسكويت",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 50000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        CapabilityKeys = new List<string> { "multiple-units", "product-barcode-settings" },
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true },
                            new ProductUnitConfigRecord { UnitId = packUnit.Id, ToBaseNumerator = 12, ToBaseDenominator = 1, SellPriceScaled = 3300000, UnitPurchasePriceScaled = 540000, IsBase = false, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                IsDefault = true,
                                IsActive = true,
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Barcode = "PACK-BC-123", BarcodeType = "internal", ProductUnitId = packUnit.Id, IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var p = await store.CreateProductAsync(create, CancellationToken.None);
                var detail = await store.GetProductDetailAsync(p.Id, CancellationToken.None);
                var packUnitRow = Assert.Single(detail.Units, u => !u.IsBase);
                Assert.True(packUnitRow.IsLocked);
                Assert.Equal(3300000, packUnitRow.SellPriceScaled);
                Assert.Equal(540000, packUnitRow.UnitPurchasePriceScaled);

                // Trying to edit numerator/ratio of the locked unit fails with conversion_locked
                var update = new UpdateProductRecord
                {
                    ProductId = p.Id,
                    Name = "بسكويت",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 50000,
                    IsActive = true,
                    Version = p.Version,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { Id = detail.Units[0].Id, UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true },
                            new ProductUnitConfigRecord { Id = packUnitRow.Id, UnitId = packUnit.Id, ToBaseNumerator = 24, ToBaseDenominator = 1, IsBase = false, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                Id = detail.Variants[0].Id,
                                IsDefault = true,
                                IsActive = true,
                                Barcodes = new List<ProductBarcodeConfigRecord>
                                {
                                    new ProductBarcodeConfigRecord { Id = detail.Variants[0].Barcodes[0].Id, Barcode = "PACK-BC-123", BarcodeType = "internal", IsPrimary = true }
                                }
                            }
                        }
                    }
                };

                var lockEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateProductAsync(update, CancellationToken.None));
                Assert.Equal("conversion_locked", lockEx.Code);

                // Trying to change base unit when barcode exists fails with base_unit_locked
                update.BaseUnitId = packUnit.Id;
                update.Configuration.Units[1].ToBaseNumerator = 12; // revert ratio
                var baseEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateProductAsync(update, CancellationToken.None));
                Assert.Equal("base_unit_locked", baseEx.Code);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Transition_from_simple_to_variant_converts_default_row_and_locks_variant_mode()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "trans-admin");
                var store = new SqliteCatalogStore(cs);

                var gradeC = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 3, Value = "S", SortOrder = 1, IsActive = true, UserId = userId }, CancellationToken.None);
                var gradeA = await store.CreateAttributeValueAsync(new CreateAttributeValueRecord { AttributeId = 3, Value = "M", SortOrder = 2, IsActive = true, UserId = userId }, CancellationToken.None);

                // Create simple product
                var create = new CreateProductRecord
                {
                    OperationKey = "create-simple-trans",
                    RequestHash = "hash-trans",
                    Name = "حذاء",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 3000000,
                    IsActive = true,
                    UserId = userId
                };

                var p = await store.CreateProductAsync(create, CancellationToken.None);
                var detailBefore = await store.GetProductDetailAsync(p.Id, CancellationToken.None);
                Assert.False(detailBefore.VariantMode);
                var defaultVarBeforeId = detailBefore.Variants[0].Id;

                // Update to variant mode
                var update = new UpdateProductRecord
                {
                    ProductId = p.Id,
                    Name = "حذاء",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 3000000,
                    IsActive = true,
                    Version = p.Version,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = true,
                        CapabilityKeys = new List<string> { "product-variants" },
                        Attributes = new List<ProductAttributeAssignmentConfigRecord>
                        {
                            new ProductAttributeAssignmentConfigRecord { AttributeId = 3, ValueIds = new List<long> { gradeC.Id, gradeA.Id }, SortOrder = 1 }
                        },
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { Id = detailBefore.Units[0].Id, UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        },
                        Variants = new List<ProductVariantConfigRecord>
                        {
                            new ProductVariantConfigRecord
                            {
                                Id = defaultVarBeforeId,
                                Name = "حذاء — S",
                                IsDefault = true,
                                IsActive = true,
                                AttributeValueIds = new List<long> { gradeC.Id }
                            },
                            new ProductVariantConfigRecord
                            {
                                Name = "حذاء — M",
                                IsDefault = false,
                                IsActive = true,
                                AttributeValueIds = new List<long> { gradeA.Id }
                            }
                        }
                    }
                };

                var updated = await store.UpdateProductAsync(update, CancellationToken.None);
                var detailAfter = await store.GetProductDetailAsync(updated.Id, CancellationToken.None);
                Assert.True(detailAfter.VariantMode);
                Assert.Equal(2, detailAfter.Variants.Count);

                var defaultVarAfter = Assert.Single(detailAfter.Variants, v => v.IsDefault);
                Assert.Equal(defaultVarBeforeId, defaultVarAfter.Id); // ID preserved!

                // Attempting to revert back to simple mode fails with variant_mode_locked
                var revert = new UpdateProductRecord
                {
                    ProductId = p.Id,
                    Name = "حذاء",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 3000000,
                    IsActive = true,
                    Version = updated.Version,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false
                    }
                };

                var revEx = await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateProductAsync(revert, CancellationToken.None));
                Assert.Equal("variant_mode_locked", revEx.Code);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Transactionality_rolls_back_entire_aggregate_on_child_write_failure()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "tx-admin");
                var store = new SqliteCatalogStore(cs);

                // Invalid ratio in unit (numerator 0)
                var invalidCreate = new CreateProductRecord
                {
                    OperationKey = "create-rollback-test",
                    RequestHash = "hash-rb",
                    Name = "منتج متراجع",
                    BaseUnitId = 1,
                    ProductType = "stock",
                    SellPriceScaled = 100000,
                    IsActive = true,
                    UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        VariantMode = false,
                        Units = new List<ProductUnitConfigRecord>
                        {
                            new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true },
                            new ProductUnitConfigRecord { UnitId = 1, ToBaseNumerator = 0, ToBaseDenominator = 1, IsBase = false, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true }
                        }
                    }
                };

                await Assert.ThrowsAsync<CatalogStoreException>(() => store.CreateProductAsync(invalidCreate, CancellationToken.None));

                using (var connection = new SQLiteConnection(cs))
                {
                    connection.Open();
                    // Verify no product was created
                    var prodCount = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Products WHERE Name='منتج متراجع'");
                    Assert.Equal(0, prodCount);
                    // Verify no audit log was created
                    var auditCount = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CatalogAuditLog");
                    Assert.Equal(0, auditCount);
                    // Verify no operation key was recorded
                    var opCount = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CatalogOperations WHERE OperationKey='create-rollback-test'");
                    Assert.Equal(0, opCount);
                }
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public async Task Simple_aggregate_create_materializes_one_exact_base_unit_and_edits()
        {
            var path = Temp();
            try
            {
                var cs = Db.ConnectionStringFor(path);
                Migrator.Run(cs);
                var userId = await CreateUserAsync(cs, "simple-form-admin");
                var store = new SqliteCatalogStore(cs);
                var product = await store.CreateAggregateProductAsync(new CreateProductRecord
                {
                    OperationKey = "simple-form", RequestHash = "simple-form-hash", Name = "تي شيرت",
                    BaseUnitId = 1, ProductType = "stock", SellPriceScaled = 5050000,
                    PurchasePriceScaled = 2000000, IsActive = true, UserId = userId,
                    Configuration = new ProductConfigurationRecord { CapabilityKeys = new List<string>(), Units = new List<ProductUnitConfigRecord>() }
                }, CancellationToken.None);
                var detail = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                var baseUnit = Assert.Single(detail.Units);
                Assert.True(baseUnit.IsBase);
                Assert.Equal(1, baseUnit.UnitId);
                Assert.Equal(1, baseUnit.ToBaseNumerator);
                Assert.Equal(1, baseUnit.ToBaseDenominator);

                await store.UpdateAggregateProductAsync(new UpdateProductRecord
                {
                    ProductId = product.Id, Name = "تي شيرت معدل", BaseUnitId = 1, ProductType = "stock",
                    SellPriceScaled = 5050000, PurchasePriceScaled = 2000000, IsActive = true,
                    Version = product.Version, UserId = userId,
                    Configuration = new ProductConfigurationRecord
                    {
                        CapabilityKeys = new List<string>(),
                        Units = new List<ProductUnitConfigRecord> { new ProductUnitConfigRecord { Id = baseUnit.Id, UnitId = 1, ToBaseNumerator = 1, ToBaseDenominator = 1, IsBase = true, IsSaleUnit = true, IsPurchaseUnit = true, IsActive = true } },
                        Variants = detail.Variants.Select(v => new ProductVariantConfigRecord { Id = v.Id, Name = v.Name, IsDefault = v.IsDefault, IsActive = v.IsActive, AttributeValueIds = v.AttributeValueIds.ToList(), Barcodes = new List<ProductBarcodeConfigRecord>() }).ToList()
                    }
                }, CancellationToken.None);
                var edited = await store.GetProductDetailAsync(product.Id, CancellationToken.None);
                Assert.Equal(baseUnit.Id, Assert.Single(edited.Units).Id);
                Assert.Equal("تي شيرت معدل", edited.Product.Name);
            }
            finally { Cleanup(path); }
        }

        private static async Task<long> CreateUserAsync(string cs, string username)
        {
            var catalog = new SqliteCatalogStore(cs);
            await catalog.CreateAttributeAsync(new CreateAttributeRecord { AttributeKey="GRADE", Name="درجة", Kind="custom", IsActive=true }, CancellationToken.None);
            await catalog.CreateAttributeAsync(new CreateAttributeRecord { AttributeKey="ORIGIN", Name="منشأ", Kind="custom", IsActive=true }, CancellationToken.None);
            var users = new SqliteUserStore(cs);
            return await users.CreateAsync(username, "مستخدم تجريبي", new PasswordHasher().Hash("secret"), "administrator", CancellationToken.None);
        }

        private static string Temp() { return Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db"); }
        private static void Cleanup(string path) { SQLiteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
