using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Alerts;
using AlKhayal.Application.Catalog;
using AlKhayal.Application.Inventory;
using AlKhayal.Data.Alerts;
using AlKhayal.Data.Catalog;
using AlKhayal.Data.Database;
using AlKhayal.Data.Inventory;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class ReorderAlertTests : IDisposable
    {
        private readonly string _path=Path.Combine(Path.GetTempPath(),"alkhayal-reorder-"+Guid.NewGuid().ToString("N")+".db");
        private readonly string _cs;
        private readonly CatalogService _catalog;
        private readonly AlertService _alerts;
        private static readonly CancellationToken Ct=CancellationToken.None;
        public ReorderAlertTests()
        {
            Assert.False(File.Exists(_path));_cs=Db.ConnectionStringFor(_path);Migrator.Run(_cs);
            _catalog=new CatalogService(new SqliteCatalogStore(_cs));_alerts=new AlertService(new SqliteAlertStore(_cs));
            using(var c=new SQLiteConnection(_cs)){c.Open();c.Execute("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('reorder-test','test-only','2026-01-01')");}
        }
        private CreateProductCommand Command(string key,string threshold=null,string type="stock") => new CreateProductCommand {OperationKey=key,Name=key,BaseUnitId=1,ProductType=type,SalePrice="1",IsActive=true,ReorderPoint=threshold};
        private async Task<CatalogProduct> Create(string key,string threshold=null,string type="stock")
        { var r=await _catalog.CreateProductAsync(Command(key,threshold,type),Ct);Assert.True(r.Succeeded,r.ErrorMessage);return r.Value; }
        private async Task<CatalogProduct> Edit(CatalogProduct p,string threshold)
        { var r=await _catalog.UpdateProductAsync(new UpdateProductCommand {ProductId=p.Id,Name=p.Name,BaseUnitId=1,ProductType=p.ProductType,SalePrice="1",IsActive=p.IsActive,Version=p.Version,ReorderPoint=threshold},Ct);Assert.True(r.Succeeded,r.ErrorMessage);return r.Value; }
        private async Task Receive(long product,long warehouse,string quantity,string key,long? variant=null)
        {
            long variantId,unit;
            using(var c=new SQLiteConnection(_cs)) {c.Open();variantId=variant??c.ExecuteScalar<long>("SELECT Id FROM ProductVariants WHERE ProductId=@product AND IsDefault=1",new{product});unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE ProductId=@product AND IsBase=1",new{product});}
            var r=await new SqliteInventoryStore(_cs).PostAsync(new InventoryPostCommand {OperationKey=key,Type="manual_receipt",WarehouseId=warehouse,UserId=1,Reason="اختبار",Lines=new[]{new InventoryLineInput{VariantId=variantId,ProductUnitId=unit,Quantity=quantity,UnitCost="1",SalePrice="1"}}},"test:"+key,Ct);
            Assert.True(r.Succeeded,r.ErrorMessage);
        }
        [Fact]
        public async Task Default_edit_disable_omitted_update_and_replay_preserve_exact_values()
        {
            var p=await Create("default");Assert.Equal("0",p.ReorderPoint);Assert.Empty(await _alerts.ActiveStockAsync(Ct));
            p=await Edit(p,"10.000001");Assert.Equal("10.000001",p.ReorderPoint);Assert.Equal(p.ReorderPoint,(await _catalog.GetProductDetailAsync(p.Id,Ct)).Value.ReorderPoint);
            p=await Edit(p,null);Assert.Equal("10.000001",p.ReorderPoint);p=await Edit(p,"0");Assert.Empty(await _alerts.ActiveStockAsync(Ct));
            await Create("replay","10");Assert.True((await _catalog.CreateProductAsync(Command("replay","10.000000"),Ct)).Succeeded);
            Assert.Equal("operation_key_conflict",(await _catalog.CreateProductAsync(Command("replay","11"),Ct)).ErrorCode);
            Assert.Equal("validation",(await _catalog.CreateProductAsync(Command("invalid","-1"),Ct)).ErrorCode);
        }
        [Fact]
        public async Task All_warehouses_and_variants_count_including_inactive_references()
        {
            var p=await Create("total","10");Assert.Single(await _alerts.ActiveStockAsync(Ct));
            var wh=await new SqliteInventoryStore(_cs).SaveWarehouseAsync(new WarehouseCommand{Name="ثان",Code="SECOND",IsActive=true},true,Ct);
            await Receive(p.Id,1,"4","first");await Receive(p.Id,wh.Value.Id,"6","second");
            Assert.Equal("10",Assert.Single(await _alerts.ActiveStockAsync(Ct)).TotalQuantity);
            long extra;
            using(var c=new SQLiteConnection(_cs)){c.Open();c.Execute(@"INSERT INTO ProductVariants(ProductId,Name,NormalizedName,IsDefault,IsActive,CreatedUtc,UpdatedUtc,CombinationKey,Version) VALUES(@Id,'آخر','آخر',0,1,'2026-01-01','2026-01-01','extra',1); UPDATE ProductDetails SET VariantMode=1 WHERE ProductId=@Id",new{p.Id});extra=c.ExecuteScalar<long>("SELECT last_insert_rowid()");}
            await Receive(p.Id,wh.Value.Id,"1","extra",extra);
            using(var c=new SQLiteConnection(_cs)){c.Open();c.Execute("UPDATE Warehouses SET IsActive=0 WHERE Id=@id;UPDATE ProductVariants SET IsActive=0 WHERE Id=@variant",new{id=wh.Value.Id,variant=extra});}
            Assert.Empty(await _alerts.ActiveStockAsync(Ct));
            p=(await _catalog.GetProductAsync(p.Id,Ct)).Value;p=await Edit(p,"11");Assert.Equal("11",Assert.Single(await _alerts.ActiveStockAsync(Ct)).TotalQuantity);
        }
        [Fact]
        public async Task Suppresses_disabled_inactive_archived_and_non_stock_products()
        {
            await Create("disabled");await Create("service","10","service");await Create("nonstock","10","non_stock");
            var inactive=await Create("inactive","10");var archived=await Create("archived","10");
            Assert.True((await _catalog.SetProductStateAsync(new SetProductStateCommand{ProductId=inactive.Id,Version=inactive.Version,IsActive=false},Ct)).Succeeded);
            Assert.True((await _catalog.ArchiveProductAsync(new ArchiveProductCommand{ProductId=archived.Id,Version=archived.Version},Ct)).Succeeded);
            var neverStocked=await Create("never","0.5");var alert=Assert.Single(await _alerts.ActiveStockAsync(Ct));Assert.Equal(neverStocked.Id,alert.ProductId);Assert.Equal("0",alert.TotalQuantity);
        }
        public void Dispose(){SQLiteConnection.ClearAllPools();foreach(var file in new[]{_path,_path+"-wal",_path+"-shm"})if(File.Exists(file))File.Delete(file);}
    }
}
