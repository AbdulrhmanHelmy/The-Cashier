using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class InstallmentProductSearchTests
    {
        [Fact]
        public async Task Paged_active_product_search_matches_name_sku_and_barcode()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var cs=Db.ConnectionStringFor(path);
            try
            {
                Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('search-user','test-only','2026-01-01T00:00:00Z')");}
                var catalog=new CatalogService(new SqliteCatalogStore(cs));var saved=await catalog.CreateProductAsync(new CreateProductCommand{OperationKey="search-product",Name="Mineral Water",BaseUnitId=1,ProductType="stock",SalePrice="8",IsActive=true},CancellationToken.None);Assert.True(saved.Succeeded,saved.ErrorMessage);
                using(var c=new SQLiteConnection(cs))
                {
                    c.Open();var variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@Id",new{Id=saved.Value.Id});var unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@Id AND IsBase=1",new{Id=saved.Value.Id});
                    c.Execute("INSERT INTO ProductSkus(ProductId,VariantId,Sku,NormalizedSku,CreatedUtc,CreatedByUserId) VALUES(@Product,@Variant,'WTR-12','WTR-12','2026-11-09T00:00:00Z',1)",new{Product=saved.Value.Id,Variant=variant});
                    c.Execute("INSERT INTO ProductBarcodes(ProductId,VariantId,ProductUnitId,Barcode,NormalizedBarcode,BarcodeType,CreatedUtc,CreatedByUserId) VALUES(@Product,@Variant,@Unit,'6221234567890','6221234567890','gtin','2026-11-09T00:00:00Z',1)",new{Product=saved.Value.Id,Variant=variant,Unit=unit});
                }
                foreach(var query in new[]{"Mineral","WTR-12","6221234567890"})
                {
                    var page=await catalog.ListProductsAsync(new ProductQuery{Search=query,IsActive=true,Page=1,PageSize=20,PosWarehouseId=1},CancellationToken.None);
                    Assert.True(page.Succeeded,page.ErrorMessage);Assert.Single(page.Value.Items);Assert.Equal(saved.Value.Id,page.Value.Items[0].Id);
                }
            }
            finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
    }
}
