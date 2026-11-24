using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Returns;
using Dapper;

namespace AlKhayal.Data.Returns
{
    public sealed class SqliteReturnReadStore : IReturnReadStore
    {
        private readonly string _cs;
        public SqliteReturnReadStore(string connectionString) { _cs = connectionString; }
        private const string Records = @"SELECT r.Id,'sales' Type,r.ReturnNumber Number,r.SalesInvoiceId InvoiceId,COALESCE(root.InvoiceNumber,i.InvoiceNumber) InvoiceNumber,COALESCE(p.Name,'نقدي مباشر') PartyName,w.Name WarehouseName,r.CreatedUtc,u.DisplayName ActorName,r.Reason,r.TotalScaled,CASE WHEN EXISTS(SELECT 1 FROM SalesElectronicRefunds e WHERE e.SalesReturnId=r.Id) THEN 0 ELSE r.CashRefundScaled END CashScaled,CASE WHEN EXISTS(SELECT 1 FROM SalesElectronicRefunds e WHERE e.SalesReturnId=r.Id) THEN r.CashRefundScaled ELSE 0 END ElectronicScaled,CASE WHEN EXISTS(SELECT 1 FROM SalesElectronicRefunds e WHERE e.SalesReturnId=r.Id) THEN CASE WHEN EXISTS(SELECT 1 FROM SalesElectronicRefundConfirmations k WHERE k.SalesReturnId=r.Id) THEN 'electronic_confirmed' ELSE 'electronic_pending' END WHEN r.CashRefundScaled=0 THEN 'credit' WHEN r.CreditReductionScaled=0 THEN 'cash' ELSE 'mixed' END Settlement
            FROM SalesReturns r JOIN SalesInvoices i ON i.Id=r.SalesInvoiceId LEFT JOIN SalesInvoices root ON root.Id=i.RootInvoiceId LEFT JOIN Customers p ON p.Id=i.CustomerId JOIN Warehouses w ON w.Id=i.WarehouseId JOIN Users u ON u.Id=r.CreatedByUserId
            UNION ALL SELECT r.Id,'purchase','PUR-RET-'||r.Id,r.PurchaseInvoiceId,i.InvoiceNumber,p.Name,w.Name,r.CreatedUtc,u.DisplayName,'',r.TotalScaled,CASE WHEN r.ReturnMode='cash_refund' THEN r.TotalScaled ELSE 0 END,0,CASE WHEN r.ReturnMode='cash_refund' THEN 'cash' ELSE 'credit' END
            FROM PurchaseReturns r JOIN PurchaseInvoices i ON i.Id=r.PurchaseInvoiceId JOIN Suppliers p ON p.Id=r.SupplierId JOIN Warehouses w ON w.Id=r.WarehouseId JOIN Users u ON u.Id=r.CreatedByUserId WHERE r.Status='posted'";
        public Task<ReturnHistoryPage> ListPageAsync(string search, long page, int pageSize, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                using (var c = new SQLiteConnection(_cs))
                {
                    await c.OpenAsync(ct).ConfigureAwait(false);
                    using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                    {
                        var source = " FROM (" + Records + ")" + (search.Length == 0 ? "" :
                            " WHERE instr(Number,@Search)>0 OR instr(InvoiceNumber,@Search)>0 OR instr(PartyName,@Search)>0");
                        var count = await c.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*)" + source,
                            new { Search = search }, tx, cancellationToken: ct)).ConfigureAwait(false);
                        var currentPage = Math.Min(page, Math.Max(1, count / pageSize + (count % pageSize == 0 ? 0 : 1)));
                        IReadOnlyList<ReturnRecord> items = Array.Empty<ReturnRecord>();
                        if (count > 0)
                        {
                            items = (await c.QueryAsync<ReturnRecord>(new CommandDefinition("SELECT *" + source +
                                " ORDER BY CreatedUtc DESC,Type,Id DESC LIMIT @Limit OFFSET @Offset",
                                new { Search = search, Limit = pageSize, Offset = (currentPage - 1) * pageSize }, tx,
                                cancellationToken: ct)).ConfigureAwait(false)).AsList();
                        }
                        tx.Commit();
                        return new ReturnHistoryPage { Items = items, Page = currentPage, PageSize = pageSize, TotalCount = count };
                    }
                }
            }, ct);
        }

        public async Task<IReadOnlyList<ReturnRecord>> ListAsync(string search, CancellationToken ct, int? searchLimit = null)
        {
            using (var c = new SQLiteConnection(_cs)) { await c.OpenAsync(ct).ConfigureAwait(false); return (await c.QueryAsync<ReturnRecord>(new CommandDefinition("SELECT * FROM (" + Records + ") WHERE @search='' OR instr(Number,@search)>0 OR instr(InvoiceNumber,@search)>0 OR instr(PartyName,@search)>0 ORDER BY CASE WHEN @Rank=1 AND Number=@search THEN 0 ELSE 1 END,CreatedUtc DESC,Type,Id DESC LIMIT @Limit", new { search, Limit = searchLimit ?? 200, Rank = searchLimit.HasValue ? 1 : 0 }, cancellationToken: ct)).ConfigureAwait(false)).ToList(); }
        }
        public async Task<long?> FindInvoiceAsync(string type, string number, CancellationToken ct)
        {
            var sql = type == "sales" ? @"SELECT si.Id FROM SalesInvoices si LEFT JOIN SalesInvoices root ON root.Id=si.RootInvoiceId
                WHERE (si.InvoiceNumber=@number OR root.InvoiceNumber=@number) AND si.Status='posted'
                AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=si.Id)
                AND NOT EXISTS(SELECT 1 FROM SalesCancellations cancellation WHERE cancellation.SalesInvoiceId=si.Id)" : "SELECT Id FROM PurchaseInvoices WHERE InvoiceNumber=@number AND Status='posted'";
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var ids = (await c.QueryAsync<long>(new CommandDefinition(sql, new { number }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                if (ids.Count > 1) throw new System.ArgumentException("رقم الفاتورة مكرر لدى أكثر من مورد؛ راجع أرقام الفواتير أولًا.");
                return ids.Count == 0 ? null : (long?)ids[0];
            }
        }
        public async Task<bool> InvoiceExistsAsync(string type, long id, CancellationToken ct)
        {
            var sql = type == "sales" ? @"SELECT EXISTS(SELECT 1 FROM SalesInvoices si WHERE si.Id=@id AND si.Status='posted'
                AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=si.Id)
                AND NOT EXISTS(SELECT 1 FROM SalesCancellations cancellation WHERE cancellation.SalesInvoiceId=si.Id))" :
                "SELECT EXISTS(SELECT 1 FROM PurchaseInvoices WHERE Id=@id AND Status='posted')";
            using (var c = new SQLiteConnection(_cs)) { await c.OpenAsync(ct).ConfigureAwait(false); return await c.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { id }, cancellationToken: ct)).ConfigureAwait(false) == 1; }
        }
        public async Task<ReturnInvoicePickerPage> SearchInvoicesAsync(ReturnInvoicePickerQuery query, CancellationToken ct)
        {
            const string source = @"SELECT si.Id,'sales' Type,si.InvoiceNumber,si.InvoiceDate,COALESCE(customer.Name,'نقدي مباشر') PartyName,si.TotalScaled,si.Status,
                    COALESCE((SELECT SUM(line.QuantityScaled) FROM SalesInvoiceLines line WHERE line.SalesInvoiceId=si.Id),0) OriginalQuantityScaled,
                    COALESCE((SELECT SUM(returnLine.QuantityScaled) FROM SalesReturnLines returnLine JOIN SalesInvoiceLines sourceLine ON sourceLine.Id=returnLine.SalesInvoiceLineId WHERE sourceLine.SalesInvoiceId=si.Id),0) ReturnedQuantityScaled
                FROM SalesInvoices si LEFT JOIN Customers customer ON customer.Id=si.CustomerId
                WHERE si.Status='posted'
                  AND NOT EXISTS(SELECT 1 FROM SalesInvoiceCorrections correction WHERE correction.PreviousInvoiceId=si.Id)
                  AND NOT EXISTS(SELECT 1 FROM SalesCancellations cancellation WHERE cancellation.SalesInvoiceId=si.Id)
                UNION ALL
                SELECT pi.Id,'purchase',pi.InvoiceNumber,pi.InvoiceDate,supplier.Name,pi.TotalScaled,pi.Status,
                    COALESCE((SELECT SUM(line.QuantityScaled) FROM PurchaseInvoiceLines line WHERE line.PurchaseInvoiceId=pi.Id),0),
                    COALESCE((SELECT SUM(returnLine.QuantityScaled) FROM PurchaseReturnLines returnLine JOIN PurchaseReturns purchaseReturn ON purchaseReturn.Id=returnLine.PurchaseReturnId WHERE purchaseReturn.PurchaseInvoiceId=pi.Id AND purchaseReturn.Status='posted'),0)
                FROM PurchaseInvoices pi JOIN Suppliers supplier ON supplier.Id=pi.SupplierId WHERE pi.Status='posted'";
            const string filtered = @" FROM (" + source + @") invoice
                WHERE (@type='all' OR invoice.Type=@type)
                  AND (@search='' OR instr(lower(invoice.InvoiceNumber),lower(@search))>0 OR instr(lower(invoice.PartyName),lower(@search))>0)
                  AND (@from='' OR invoice.InvoiceDate>=@from) AND (@to='' OR invoice.InvoiceDate<=@to)
                  AND (@status='all'
                    OR (@status='eligible' AND invoice.OriginalQuantityScaled>invoice.ReturnedQuantityScaled)
                    OR (@status='partial' AND invoice.ReturnedQuantityScaled>0 AND invoice.OriginalQuantityScaled>invoice.ReturnedQuantityScaled)
                    OR (@status='full' AND invoice.OriginalQuantityScaled<=invoice.ReturnedQuantityScaled))";
            var args = new { type = query.Type, search = query.Search, from = query.From ?? "", to = query.To ?? "", status = query.Status, take = query.PageSize, skip = (query.Page - 1) * query.PageSize };
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                var total = await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*)" + filtered, args, cancellationToken: ct)).ConfigureAwait(false);
                var items = (await c.QueryAsync<ReturnInvoicePickerItem>(new CommandDefinition(@"SELECT invoice.*,CASE WHEN invoice.OriginalQuantityScaled>invoice.ReturnedQuantityScaled THEN invoice.OriginalQuantityScaled-invoice.ReturnedQuantityScaled ELSE 0 END RemainingQuantityScaled" + filtered + " ORDER BY invoice.InvoiceDate DESC,invoice.Id DESC LIMIT @take OFFSET @skip", args, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                foreach (var item in items)
                {
                    item.IsEligible = item.RemainingQuantityScaled > 0;
                    item.Eligibility = item.IsEligible ? (item.ReturnedQuantityScaled > 0 ? "partial" : "eligible") : "fully_returned";
                }
                return new ReturnInvoicePickerPage { Items = items, Page = query.Page, PageSize = query.PageSize, TotalCount = total };
            }
        }
        public async Task<string> GetContractNumberAsync(long contractId,CancellationToken ct)
        {
            using(var c=new SQLiteConnection(_cs)){await c.OpenAsync(ct).ConfigureAwait(false);return await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT ContractNumber FROM CustomerInstallmentContracts WHERE Id=@Id",new{Id=contractId},cancellationToken:ct)).ConfigureAwait(false);}
        }
        public async Task<List<ReturnInstallment>> GetInstallmentsAsync(long contractId,CancellationToken ct)
        {
            using(var c=new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                return (await c.QueryAsync<ReturnInstallment>(new CommandDefinition(@"SELECT s.InstallmentNumber Number,s.DueOn,
                    s.AmountScaled-COALESCE((SELECT SUM(r.AmountScaled) FROM CustomerInstallmentReturnAllocations r WHERE r.ContractId=s.ContractId AND r.InstallmentNumber=s.InstallmentNumber),0)
                    -COALESCE((SELECT SUM(p.AmountScaled) FROM (SELECT ContractId,InstallmentNumber,AmountScaled FROM CustomerInstallmentPayments UNION ALL SELECT ContractId,InstallmentNumber,AmountScaled FROM CustomerInstallmentPartialPayments) p WHERE p.ContractId=s.ContractId AND p.InstallmentNumber=s.InstallmentNumber),0) RemainingScaled
                    FROM CustomerInstallmentSchedule s WHERE s.ContractId=@Id ORDER BY s.InstallmentNumber",new{Id=contractId},cancellationToken:ct)).ConfigureAwait(false)).ToList();
            }
        }
        public async Task<ReturnRecord> GetAsync(string type, long id, CancellationToken ct)
        {
            using (var c = new SQLiteConnection(_cs))
            {
                await c.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    var result = await c.QuerySingleOrDefaultAsync<ReturnRecord>(new CommandDefinition("SELECT * FROM (" + Records + ") WHERE Type=@type AND Id=@id", new { type, id }, tx, cancellationToken: ct)).ConfigureAwait(false); if (result == null) return null;
                    if(type=="sales")
                    {
                        var electronic=await c.QuerySingleOrDefaultAsync<ElectronicDetail>(new CommandDefinition(@"SELECT e.Method ElectronicMethod,p.Name ElectronicInstrument,e.OriginalPaymentKey,
                            CASE WHEN k.SalesReturnId IS NULL THEN 'pending' ELSE 'confirmed' END ElectronicRefundStatus,k.ExternalReference ExternalRefundReference
                            FROM SalesElectronicRefunds e JOIN PaymentInstruments p ON p.Id=e.InstrumentId LEFT JOIN SalesElectronicRefundConfirmations k ON k.SalesReturnId=e.SalesReturnId WHERE e.SalesReturnId=@id",new{id},tx,cancellationToken:ct)).ConfigureAwait(false);
                        if(electronic!=null){result.ElectronicMethod=electronic.ElectronicMethod;result.ElectronicInstrument=electronic.ElectronicInstrument;result.OriginalPaymentKey=electronic.OriginalPaymentKey;result.ElectronicRefundStatus=electronic.ElectronicRefundStatus;result.ExternalRefundReference=electronic.ExternalRefundReference;}
                    }
                    var sql = type == "sales" ? @"SELECT rl.Id,p.Name ProductName,CASE WHEN (SELECT SellingType FROM ProductDetails WHERE ProductId=v.ProductId)='weight' THEN 'كجم' ELSE u.Name END UnitName,rl.QuantityScaled,rl.AmountScaled,
                        CASE WHEN EXISTS(SELECT 1 FROM SalesReturnCostAllocations allocation JOIN InventoryCostLayers layer ON layer.Id=allocation.CostLayerId WHERE allocation.SalesReturnLineId=rl.Id AND layer.IsSellable=0) THEN 0 ELSE 1 END IsSellable,'restored' InventoryStatus
                        FROM SalesReturnLines rl JOIN SalesInvoiceLines il ON il.Id=rl.SalesInvoiceLineId JOIN ProductVariants v ON v.Id=il.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=il.ProductUnitId JOIN Units u ON u.Id=pu.UnitId WHERE rl.SalesReturnId=@id ORDER BY rl.Id" : @"SELECT rl.Id,p.Name ProductName,CASE WHEN (SELECT SellingType FROM ProductDetails WHERE ProductId=v.ProductId)='weight' THEN 'كجم' ELSE u.Name END UnitName,rl.QuantityScaled,rl.AccountAmountScaled AmountScaled,NULL IsSellable,'returned_to_supplier' InventoryStatus FROM PurchaseReturnLines rl JOIN PurchaseInvoiceLines il ON il.Id=rl.PurchaseInvoiceLineId JOIN ProductVariants v ON v.Id=il.VariantId JOIN Products p ON p.Id=v.ProductId JOIN ProductUnits pu ON pu.Id=il.ProductUnitId JOIN Units u ON u.Id=pu.UnitId WHERE rl.PurchaseReturnId=@id ORDER BY rl.Id";
                    var lines = (await c.QueryAsync<LineRow>(new CommandDefinition(sql, new { id }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    foreach (var line in lines)
                    {
                        if (type == "sales") line.Serials = (await c.QueryAsync<string>(new CommandDefinition("SELECT Serial FROM SalesReturnPhysicalUnits WHERE SalesReturnLineId=@Id", new { line.Id }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                        else line.Serials = (await c.QueryAsync<string>(new CommandDefinition(@"SELECT ip.DisplayValue FROM PurchaseReturnLines rl JOIN PurchaseReturns r ON r.Id=rl.PurchaseReturnId JOIN PurchaseInvoiceLines il ON il.Id=rl.PurchaseInvoiceLineId JOIN InventoryDocumentLines dl ON dl.DocumentId=r.InventoryDocumentId AND dl.LineNumber=(SELECT COUNT(*) FROM PurchaseReturnLines previous WHERE previous.PurchaseReturnId=rl.PurchaseReturnId AND previous.Id<=rl.Id) AND dl.VariantId=il.VariantId AND dl.ProductUnitId=il.ProductUnitId JOIN StockMovements sm ON sm.DocumentLineId=dl.Id JOIN StockMovementUnits mu ON mu.MovementId=sm.Id JOIN InventoryIdentifiers ip ON ip.PhysicalUnitId=mu.PhysicalUnitId AND ip.Kind='serial' WHERE rl.Id=@Id", new { line.Id }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    }
                    result.Lines = lines.Select(x => new ReturnRecordLine { ProductName = x.ProductName, UnitName = x.UnitName, QuantityScaled = x.QuantityScaled, AmountScaled = x.AmountScaled, IsSellable = x.IsSellable, InventoryStatus = x.InventoryStatus, Serials = x.Serials }).ToList();
                    result.Movements = (await c.QueryAsync<ReturnMoneyMovement>(new CommandDefinition("SELECT m.Id,a.Name AccountName,m.Type,l.Role,m.AmountScaled,m.CreatedUtc,'TR-'||m.Id Reference FROM ReturnTreasuryLinks l JOIN TreasuryMovements m ON m.Id=l.MovementId JOIN TreasuryAccounts a ON a.Id=m.AccountId WHERE (@type='sales' AND l.SalesReturnId=@id) OR (@type='purchase' AND l.PurchaseReturnId=@id) ORDER BY l.Id", new { type, id }, tx, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    tx.Commit(); return result;
                }
            }
        }
        private sealed class LineRow { public long Id { get; set; } public string ProductName { get; set; } public string UnitName { get; set; } public long QuantityScaled { get; set; } public long AmountScaled { get; set; } public bool? IsSellable { get; set; } public string InventoryStatus { get; set; } public List<string> Serials { get; set; } = new List<string>(); }
        private sealed class ElectronicDetail { public string ElectronicMethod { get; set; } public string ElectronicInstrument { get; set; } public string OriginalPaymentKey { get; set; } public string ElectronicRefundStatus { get; set; } public string ExternalRefundReference { get; set; } }
    }
}
