using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Inventory;
using AlKhayal.Data.Inventory;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class EstimatedProfitTests
    {
        [Fact]
        public async Task Shortfall_uses_saved_purchase_cost_and_reports_agree_after_price_edit()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                using(var c=f.Db.Open())c.Execute("UPDATE ProductDetails SET AllowSaleWhenOutOfStock=1");
                await f.Receipt("2","3");
                var sale=await f.Sale("5");
                using(var c=f.Db.Open())
                {
                    var parts=c.Query<(string Basis,long QuantityBaseScaled,long ValueScaled)>("SELECT Basis,QuantityBaseScaled,ValueScaled FROM SalesInvoiceCostParts WHERE SalesInvoiceLineId=@Id ORDER BY Id",new{Id=sale.Lines.Single().Id}).ToList();
                    Assert.Equal(2,parts.Count);
                    Assert.Equal("actual",parts[0].Basis);
                    Assert.Equal(2000000,parts[0].QuantityBaseScaled);
                    Assert.Equal(60000,parts[0].ValueScaled);
                    Assert.Equal("estimated_purchase",parts[1].Basis);
                    Assert.Equal(3000000,parts[1].QuantityBaseScaled);
                    Assert.Equal(90000,parts[1].ValueScaled);
                }
                await f.Price("99");
                var today=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var net=await f.Report(today,today);
                var products=await StockReportTests.Query(f,"product-profit");
                Assert.Equal("15.0000",net.Summary.Cogs);
                Assert.Equal("35.0000",net.Summary.GrossProfit);
                Assert.Equal(net.Summary.GrossProfit,products.Summary.Profit);
            }
        }

        [Fact]
        public async Task Entirely_unstocked_sale_uses_configured_cost_and_partial_returns_reverse_saved_estimate()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                using(var c=f.Db.Open())c.Execute("UPDATE ProductDetails SET AllowSaleWhenOutOfStock=1");
                var sale=await f.Sale("3");
                using(var c=f.Db.Open())Assert.Equal("estimated_configured",c.ExecuteScalar<string>("SELECT Basis FROM SalesInvoiceCostParts WHERE SalesInvoiceLineId=@Id",new{Id=sale.Lines.Single().Id}));
                for(var n=0;n<2;n++)
                {
                    var result=await f.Sales.ReturnAsync(sale.Id,new SaleReturnCommand{OperationKey="estimated-return-"+n,Reason="إرجاع جزئي",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=sale.Lines.Single().Id,Quantity="1"}}},f.Db.UserId,System.Threading.CancellationToken.None);
                    Assert.True(result.Succeeded,result.ErrorMessage);
                }
                using(var c=f.Db.Open())Assert.Equal(40000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnEstimatedCosts"));
                var today=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var net=await f.Report(today,today);
                Assert.True(net.Summary.Complete);
                Assert.Equal("2.0000",net.Summary.Cogs);
                Assert.Equal(net.Summary.GrossProfit,(await StockReportTests.Query(f,"product-profit")).Summary.Profit);
            }
        }

        [Fact]
        public async Task Latest_purchase_cost_prefers_the_sale_warehouse_and_otherwise_uses_other_warehouses()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                long variant,unit;
                using(var c=f.Db.Open())
                {
                    c.Execute("UPDATE ProductDetails SET AllowSaleWhenOutOfStock=1");
                    variant=c.ExecuteScalar<long>("SELECT Id FROM ProductVariants LIMIT 1");
                    unit=c.ExecuteScalar<long>("SELECT Id FROM ProductUnits WHERE IsBase=1 LIMIT 1");
                }
                var warehouse=await new SqliteInventoryStore(f.Cs).SaveWarehouseAsync(new WarehouseCommand{Code="W-EST",Name="ثان"},true,System.Threading.CancellationToken.None);
                Assert.True(warehouse.Succeeded);
                var other=warehouse.Value.Id;
                var receipt=new InventoryPostCommand{OperationKey="other-estimate",Type="manual_receipt",WarehouseId=other,UserId=f.Db.UserId,Lines=new[]{new InventoryLineInput{VariantId=variant,ProductUnitId=unit,Quantity="1",UnitCost="7",SalePrice="10"}}};
                Assert.True((await new SqliteInventoryStore(f.Cs).PostAsync(receipt,InventoryService.CanonicalHashPost(receipt),System.Threading.CancellationToken.None)).Succeeded);
                var first=await f.Sale("1");
                using(var c=f.Db.Open())Assert.Equal(70000,c.ExecuteScalar<long>("SELECT ValueScaled FROM SalesInvoiceCostParts WHERE SalesInvoiceLineId=@Id",new{Id=first.Lines.Single().Id}));
                await f.Receipt("1","3");
                var second=await f.Sale("2");
                using(var c=f.Db.Open())Assert.Equal(30000,c.ExecuteScalar<long>("SELECT ValueScaled FROM SalesInvoiceCostParts WHERE SalesInvoiceLineId=@Id AND Basis='estimated_purchase'",new{Id=second.Lines.Single().Id}));
            }
        }

        [Fact]
        public async Task Unknown_purchase_cost_makes_profit_incomplete_without_zero_cost()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                using(var c=f.Db.Open())c.Execute("UPDATE ProductDetails SET AllowSaleWhenOutOfStock=1,PurchasePriceScaled=NULL");
                var sale=await f.Sale("1");
                using(var c=f.Db.Open())
                {
                    Assert.Equal("unknown",c.ExecuteScalar<string>("SELECT Basis FROM SalesInvoiceCostParts WHERE SalesInvoiceLineId=@Id",new{Id=sale.Lines.Single().Id}));
                    Assert.Null(c.ExecuteScalar<long?>("SELECT ValueScaled FROM SalesInvoiceCostParts WHERE SalesInvoiceLineId=@Id",new{Id=sale.Lines.Single().Id}));
                }
                var today=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var net=await f.Report(today,today);
                Assert.Null(net.Summary.GrossProfit);
                Assert.Equal(1,net.Summary.MissingCostCount);
            }
        }

        [Fact]
        public async Task Cancel_unstocked_credit_sale_reverses_saved_profit_without_creating_inventory()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                using(var c=f.Db.Open())c.Execute("UPDATE ProductDetails SET AllowSaleWhenOutOfStock=1");
                var sale=await f.Sale("2");
                var cancel=await f.Sales.CancelAsync(sale.Id,"cancel-estimated","إلغاء",f.Db.UserId,System.Threading.CancellationToken.None);
                Assert.True(cancel.Succeeded,cancel.ErrorMessage);
                using(var c=f.Db.Open())Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM StockMovements WHERE DocumentId=(SELECT InventoryDocumentId FROM SalesCancellations WHERE SalesInvoiceId=@Id)",new{Id=sale.Id}));
                var today=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var net=await f.Report(today,today);
                Assert.True(net.Summary.Complete);
                Assert.Equal("0.0000",net.Summary.GrossProfit);
                Assert.Equal(net.Summary.GrossProfit,(await StockReportTests.Query(f,"product-profit")).Summary.Profit);
            }
        }

        [Fact]
        public async Task Mixed_stock_and_estimate_repeated_returns_reverse_each_original_cost_once()
        {
            using(var f=await NetProfitReportTests.Fixture.Create())
            {
                using(var c=f.Db.Open())c.Execute("UPDATE ProductDetails SET AllowSaleWhenOutOfStock=1");
                await f.Receipt("2","3");
                var sale=await f.Sale("5");
                foreach(var quantity in new[]{"1","2","2"})
                {
                    var result=await f.Sales.ReturnAsync(sale.Id,new SaleReturnCommand{OperationKey="mixed-return-"+(Guid.NewGuid().ToString("N")),Reason="إرجاع جزئي",Lines=new List<SaleReturnLineCommand>{new SaleReturnLineCommand{InvoiceLineId=sale.Lines.Single().Id,Quantity=quantity}}},f.Db.UserId,System.Threading.CancellationToken.None);
                    Assert.True(result.Succeeded,result.ErrorMessage);
                }
                using(var c=f.Db.Open())
                {
                    Assert.Equal(60000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnCostAllocations"));
                    Assert.Equal(90000,c.ExecuteScalar<long>("SELECT SUM(ValueScaled) FROM SalesReturnEstimatedCosts"));
                    Assert.Equal(2000000,c.ExecuteScalar<long>("SELECT COALESCE(SUM(QuantityBaseScaled),0) FROM StockMovements WHERE DocumentId IN (SELECT InventoryDocumentId FROM SalesReturns WHERE SalesInvoiceId=@Id)",new{Id=sale.Id}));
                }
                var today=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var net=await f.Report(today,today);
                Assert.True(net.Summary.Complete);
                Assert.Equal("0.0000",net.Summary.GrossProfit);
                Assert.Equal(net.Summary.GrossProfit,(await StockReportTests.Query(f,"product-profit")).Summary.Profit);
            }
        }
    }
}
