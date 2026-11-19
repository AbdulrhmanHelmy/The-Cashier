using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Features;
using AlKhayal.Application.Inventory;
using AlKhayal.Application.Printing;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Inventory;
using AlKhayal.Data.Printing;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class CatalogRetiredAttributeTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;

        [Fact]
        public async Task Retired_attributes_and_capabilities_are_unavailable_for_reads_and_writes()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var store = new SqliteCatalogStore(f.ConnectionString);
                Assert.Empty(await store.ListAttributesAsync(null, true, Ct));
                Assert.Null(await store.GetAttributeAsync(1, Ct));
                Assert.Null(await store.GetAttributeAsync(2, Ct));
                foreach (var id in new long[] { 1, 2 })
                {
                    await Assert.ThrowsAsync<CatalogStoreException>(() => store.CreateAttributeValueAsync(
                        new CreateAttributeValueRecord { AttributeId=id, Value="retired", IsActive=true, UserId=f.UserId }, Ct));
                    await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateAttributeAsync(
                        new UpdateAttributeRecord { Id=id, Name="retired", IsActive=true, Version=1, UserId=f.UserId }, Ct));
                }
                foreach (var key in new[] { "sizes", "colors" })
                {
                    Assert.Null(FeatureRegistry.Find(key));
                    var result = await new CatalogService(store).CreateProductAsync(new CreateProductCommand {
                        OperationKey=Guid.NewGuid().ToString(), Name="retired", BaseUnitId=1, ProductType="stock", SalePrice="1", UserId=f.UserId,
                        Configuration=new ProductConfigurationCommand { CapabilityKeys=new[] { key } }
                    }, Ct);
                    Assert.Equal("capability_not_supported", result.ErrorCode);
                }
                Assert.DoesNotContain(typeof(CatalogProduct).GetProperties(), p=>p.Name=="SizeValues"||p.Name=="ColorValues");
                Assert.DoesNotContain(typeof(AttributeValueDto).GetProperties(), p=>p.Name=="ColorHex");
                Assert.DoesNotContain(typeof(ProductAttributeAssignmentCommand).GetProperties(), p=>p.Name=="Values");
                var custom = await CustomAttributeFixture.Create(f.ConnectionString, f.UserId);
                var value = await store.GetAttributeValueAsync(custom.ValueIds[0], Ct);
                var changed = await store.UpdateAttributeValueAsync(new UpdateAttributeValueRecord {
                    Id=value.Id, Value="Updated grade", Version=value.Version, IsActive=true, UserId=f.UserId
                }, Ct);
                Assert.Equal("Updated grade", changed.Value);
                Assert.Equal("UPDATED GRADE", changed.NormalizedValue);
            }
        }

        [Fact]
        public async Task Legacy_axes_and_stock_history_survive_normal_edit_without_exposing_or_rebuilding_combinations()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var store = new SqliteCatalogStore(f.ConnectionString);
                var service = new CatalogService(store);
                var p = await LabelPersistenceTests.Product(f);
                var variant = p.Variants.Single();
                var unit = p.Units.Single();
                var receipt = new InventoryPostCommand { OperationKey="legacy-stock", Type="manual_receipt", WarehouseId=1, UserId=f.UserId,
                    Lines=new[] { new InventoryLineInput { VariantId=variant.Id, ProductUnitId=unit.Id, Quantity="2", UnitCost="5", SalePrice="12.50" } } };
                var posted = await new SqliteInventoryStore(f.ConnectionString).PostAsync(receipt, InventoryService.CanonicalHashPost(receipt), Ct);
                Assert.True(posted.Succeeded, posted.ErrorMessage);
                // Seed an old deployed database directly; retired application APIs must never create this data.
                using (var c = f.Open())
                {
                    c.Execute(@"INSERT INTO ProductAttributeValues(Id,AttributeId,Value,NormalizedValue,ColorHex,CreatedUtc,UpdatedUtc)
                        VALUES(9001,1,'M','M',NULL,@Now,@Now),(9002,2,'Blue','BLUE','#0000FF',@Now,@Now),(9003,1,'L','L',NULL,@Now,@Now);
                        INSERT INTO ProductAttributeAssignments(ProductId,AttributeId,CreatedUtc,UpdatedUtc) VALUES(@Id,1,@Now,@Now),(@Id,2,@Now,@Now);
                        INSERT INTO ProductAttributeValueAssignments(ProductId,AttributeId,AttributeValueId,CreatedUtc) VALUES(@Id,1,9001,@Now),(@Id,2,9002,@Now),(@Id,1,9003,@Now);
                        INSERT INTO ProductVariantValues(VariantId,ProductId,AttributeId,AttributeValueId) VALUES(@Variant,@Id,1,9001),(@Variant,@Id,2,9002);
                        UPDATE ProductVariants SET CombinationKey='1:9001|2:9002' WHERE Id=@Variant;
                        INSERT INTO ProductVariants(Id,ProductId,Name,NormalizedName,IsDefault,IsActive,CombinationKey,SellPriceOverrideScaled,CreatedUtc,UpdatedUtc)
                        VALUES(9001,@Id,'Legacy L','LEGACY L',0,1,'1:9003|2:9002',173456,@Now,@Now);
                        INSERT INTO ProductVariantValues(VariantId,ProductId,AttributeId,AttributeValueId) VALUES(9001,@Id,1,9003),(9001,@Id,2,9002);
                        UPDATE ProductDetails SET VariantMode=1 WHERE ProductId=@Id;
                        INSERT INTO ProductCapabilityAssignments(ProductId,CapabilityKey,IsEnabled,AssignedUtc,UpdatedUtc,ChangedByUserId)
                        VALUES(@Id,'sizes',1,@Now,@Now,@User),(@Id,'colors',1,@Now,@Now,@User),(@Id,'product-variants',1,@Now,@Now,@User),
                        (@Id,'expiry-dates',1,@Now,@Now,@User),(@Id,'batch-tracking',1,@Now,@Now,@User),
                        (@Id,'serial-numbers',1,@Now,@Now,@User),(@Id,'imei-tracking',1,@Now,@Now,@User);",
                        new { Id=p.Id, Variant=variant.Id, Now=DateTime.UtcNow.ToString("o"), User=f.UserId });
                }
                var before = Snapshot(f);
                p = (await service.GetProductDetailAsync(p.Id, Ct)).Value;
                Assert.Empty(p.Attributes);
                Assert.Equal(2, p.Variants.Count);
                Assert.All(p.Variants, v=>Assert.Empty(v.AttributeValueIds));
                Assert.DoesNotContain(p.Capabilities, x=>x.Key=="sizes"||x.Key=="colors");
                var config = Configuration(p);
                var updated = await service.UpdateProductAsync(new UpdateProductCommand {
                    ProductId=p.Id, Name="بيانات معدلة", BaseUnitId=p.BaseUnitId, ProductType=p.ProductType, SalePrice="15", PurchasePrice=p.PurchasePrice,
                    IsActive=true, Version=p.Version, UserId=f.UserId, Configuration=config
                }, Ct);
                Assert.True(updated.Succeeded, updated.ErrorMessage);
                Assert.Equal(before, Snapshot(f));
                var summary = (await service.ListProductsAsync(new ProductQuery(), Ct)).Value.Items.Single();
                Assert.True(summary.HasExpiryTracking && summary.HasBatchTracking && summary.HasSerialTracking && summary.HasImeiTracking);
                // A stale caller cannot introduce a replacement combination or archive the historical identity.
                p = (await service.GetProductDetailAsync(p.Id, Ct)).Value;
                config = Configuration(p);
                config.Variants.First().Archive=true;
                var rejected = await service.UpdateProductAsync(new UpdateProductCommand {
                    ProductId=p.Id, Name=p.Name, BaseUnitId=p.BaseUnitId, ProductType=p.ProductType, SalePrice=p.SalePrice,
                    IsActive=true, Version=p.Version, UserId=f.UserId, Configuration=config
                }, Ct);
                Assert.Equal("legacy_configuration_locked", rejected.ErrorCode);
                Assert.Equal(before, Snapshot(f));
                await Assert.ThrowsAsync<CatalogStoreException>(() => store.UpdateAttributeValueAsync(
                    new UpdateAttributeValueRecord { Id=9002, Value="changed", Version=1, IsActive=true, UserId=f.UserId }, Ct));
                using (var c=f.Open()) Assert.Empty(c.Query<dynamic>("PRAGMA foreign_key_check"));
            }
        }

        private static ProductConfigurationCommand Configuration(CatalogProductDetail p)
        {
            return new ProductConfigurationCommand{
                VariantMode=p.VariantMode,CapabilityKeys=p.Capabilities.Where(x=>x.IsAssigned).Select(x=>x.Key).ToArray(),
                Attributes=p.Attributes.Select(a=>new ProductAttributeAssignmentCommand{AttributeId=a.AttributeId,ValueIds=a.Values.Select(v=>v.ValueId).ToArray()}).ToArray(),
                Units=p.Units.Select(u=>new ProductUnitCommand{Id=u.Id,UnitId=u.UnitId,Numerator=u.Numerator,Denominator=u.Denominator,IsBase=u.IsBase,IsSaleUnit=u.IsSaleUnit,IsPurchaseUnit=u.IsPurchaseUnit,IsActive=u.IsActive,Version=u.Version}).ToArray(),
                Variants=p.Variants.Select(v=>new ProductVariantCommand{Id=v.Id,Name=v.Name,AttributeValueIds=v.AttributeValueIds,IsDefault=v.IsDefault,IsActive=v.IsActive,Archive=v.IsArchived,SalePriceOverride=v.SalePriceOverride,PurchasePriceOverride=v.PurchasePriceOverride,Sku=v.Sku,Barcodes=v.Barcodes.Select(b=>new ProductBarcodeCommand{Id=b.Id,Code=b.Code,Type=b.Type,ProductUnitId=b.ProductUnitId,IsPrimary=b.IsPrimary,Retire=b.IsRetired}).ToArray()}).ToArray()
            };
        }

        private static string Snapshot(TreasuryPersistenceTests.Fixture f)
        {
            // Compare complete persisted rows, including legacy timestamps and immutable financial/stock evidence.
            using (var c=f.Open())
                return string.Join("\n", new[] { "ProductAttributes", "ProductAttributeValues", "ProductAttributeAssignments", "ProductAttributeValueAssignments", "ProductVariantValues", "ProductVariants", "ProductCapabilityAssignments", "StockMovements", "InventoryCostLayers" }
                    .Select(table=>table+":"+Newtonsoft.Json.JsonConvert.SerializeObject(c.Query("SELECT * FROM "+table+(table=="ProductCapabilityAssignments"?" WHERE CapabilityKey IN ('sizes','colors')":"")+" ORDER BY rowid"))));
        }

        [Fact]
        public async Task Stored_label_templates_ignore_retired_elements_without_rewriting_saved_json()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var repository = new SqliteLabelRepository(f.ConnectionString);
                var settings = LabelService.Defaults();
                settings.Templates[0].Elements.Add(new LabelElement { Key="size", Visible=true });
                settings.Templates[0].Elements.Add(new LabelElement { Key="color", Visible=true });
                await repository.SaveAsync(settings, f.UserId, Ct);
                string before;
                using (var c=f.Open()) before=c.ExecuteScalar<string>("SELECT Json FROM LabelConfiguration");
                var service = LabelPersistenceTests.Service(f, new LabelPersistenceTests.Backend(), out var printing);
                var projected = await service.SettingsAsync(Ct);
                Assert.DoesNotContain(projected.Templates[0].Elements, x=>x.Key=="size"||x.Key=="color");
                LabelService.ValidateTemplate(projected.Templates[0]);
                using (var c=f.Open()) Assert.Equal(before,c.ExecuteScalar<string>("SELECT Json FROM LabelConfiguration"));
                Assert.False(LabelService.Sample().Items[0].Fields.ContainsKey("size"));
                Assert.False(LabelService.Sample().Items[0].Fields.ContainsKey("color"));
            }
        }
    }
}
