using System.Collections.Generic;
using System.Data.SQLite;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Alerts;
using Dapper;

namespace AlKhayal.Data.Alerts
{
    public sealed class SqliteAlertStore : IAlertStore
    {
        private readonly string _connectionString;
        public SqliteAlertStore(string connectionString) { _connectionString=connectionString; }
        public async Task<IReadOnlyList<AlertRecord>> ActiveAsync(long userId, DateTime asOfUtc, CancellationToken ct)
        {
            using(var connection=new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var businessNow=asOfUtc.Kind==DateTimeKind.Utc?asOfUtc.ToLocalTime():asOfUtc;
                var today=businessNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var until=businessNow.Date.AddDays(30).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var rows=await connection.QueryAsync<AlertRecord>(new CommandDefinition(@"
                    WITH Stock AS (
                        SELECT p.Id,p.Name,CASE WHEN d.SellingType='weight' THEN 'كجم' ELSE u.Name END Unit,d.ReorderPointScaled,
                            COALESCE((SELECT SUM(b.QuantityBaseScaled) FROM ProductVariants v
                                JOIN InventoryBalances b ON b.VariantId=v.Id WHERE v.ProductId=p.Id),0) TotalQuantityScaled
                        FROM Products p JOIN ProductDetails d ON d.ProductId=p.Id
                        JOIN Units u ON u.Id=d.BaseUnitId
                        WHERE d.ReorderPointScaled>0 AND d.ProductType='stock' AND d.IsActive=1 AND d.ArchivedUtc IS NULL
                    )
                    SELECT 'stock:'||Id Id,Id ProductId,Name ProductName,Unit,
                        CASE WHEN TotalQuantityScaled=0 THEN '0' ELSE rtrim(rtrim(printf('%.6f',TotalQuantityScaled/1000000.0),'0'),'.') END TotalQuantity,
                        CASE WHEN ReorderPointScaled=0 THEN '0' ELSE rtrim(rtrim(printf('%.6f',ReorderPointScaled/1000000.0),'0'),'.') END ReorderPoint,
                        CASE WHEN TotalQuantityScaled<=0 THEN 'low-stock' ELSE 'reorder' END Type,
                        CASE WHEN TotalQuantityScaled<=0 THEN 'انتهى مخزون '||Name ELSE 'بلغ الصنف حد إعادة الطلب: '||Name END Title,
                        'الإجمالي: '||printf('%.6f',TotalQuantityScaled/1000000.0)||' '||Unit||
                            ' · حد إعادة الطلب: '||printf('%.6f',ReorderPointScaled/1000000.0) Detail,
                        CASE WHEN TotalQuantityScaled<=0 THEN 'خطر' ELSE 'تحذير' END Severity,
                        NULL Date,
                        CASE WHEN rs.AlertId IS NULL THEN 0 ELSE 1 END Read,
                        'product-edit' RelatedRoute,Id RelatedId,0 RelatedParentId,
                        0 BatchId,NULL LotNumber,0 WarehouseId,NULL WarehouseName
                    FROM Stock LEFT JOIN AlertReadStates rs ON rs.UserId=@userId AND rs.AlertId='stock:'||Id
                    WHERE TotalQuantityScaled<=ReorderPointScaled
                    UNION ALL
                    SELECT 'expiry:'||pb.Id||':'||w.Id Id,p.Id ProductId,p.Name ProductName,u.Name Unit,
                        CASE WHEN b.QuantityBaseScaled=0 THEN '0' ELSE rtrim(rtrim(printf('%.6f',b.QuantityBaseScaled/1000000.0),'0'),'.') END TotalQuantity,
                        NULL ReorderPoint,'expiry',
                        CASE WHEN pb.ExpirationDate<@today THEN 'انتهت صلاحية '||p.Name ELSE 'اقتربت صلاحية '||p.Name END,
                        'تاريخ الصلاحية: '||pb.ExpirationDate||' · المخزن: '||w.Name||' · الكمية المتبقية: '||
                            rtrim(rtrim(printf('%.6f',b.QuantityBaseScaled/1000000.0),'0'),'.')||' '||u.Name||' · التاريخ: '||pb.ExpirationDate,
                        CASE WHEN pb.ExpirationDate<@today THEN 'خطر' ELSE 'تحذير' END,
                        pb.ExpirationDate,
                        CASE WHEN rs.AlertId IS NULL THEN 0 ELSE 1 END,
                        'product-edit',p.Id,0,pb.Id BatchId,pb.LotNumber,w.Id WarehouseId,w.Name WarehouseName
                    FROM ProductBatches pb JOIN ProductVariants v ON v.Id=pb.VariantId
                    JOIN Products p ON p.Id=v.ProductId JOIN ProductDetails d ON d.ProductId=p.Id
                    JOIN Units u ON u.Id=d.BaseUnitId JOIN InventoryBalances b ON b.BatchId=pb.Id
                    JOIN Warehouses w ON w.Id=b.WarehouseId
                    LEFT JOIN AlertReadStates rs ON rs.UserId=@userId AND rs.AlertId='expiry:'||pb.Id||':'||w.Id
                    WHERE d.ProductType='stock' AND d.IsActive=1 AND d.ArchivedUtc IS NULL
                        AND pb.ExpirationDate IS NOT NULL AND pb.ExpirationDate<=@until AND b.QuantityBaseScaled>0
                    UNION ALL
                    SELECT 'installment:'||s.ContractId||':'||s.InstallmentNumber,0,NULL,NULL,NULL,NULL,'installment',
                        'قسط مستحق للعميل: '||c.Name,
                        'الرصيد غير المسدد: '||printf('%.2f',(s.AmountScaled-COALESCE(red.AmountScaled,0)-COALESCE(pay.AmountScaled,0))/10000.0)||' · تاريخ الاستحقاق: '||s.DueOn,
                        CASE WHEN s.DueOn<@today THEN 'خطر' ELSE 'تحذير' END,
                        s.DueOn,
                        CASE WHEN rs.AlertId IS NULL THEN 0 ELSE 1 END,
                        'installment-payment',s.ContractId,ic.CustomerId,0,NULL,0,NULL
                    FROM CustomerInstallmentSchedule s JOIN CustomerInstallmentContracts ic ON ic.Id=s.ContractId
                    JOIN Customers c ON c.Id=ic.CustomerId
                    LEFT JOIN (SELECT ContractId,InstallmentNumber,SUM(AmountScaled) AmountScaled FROM (SELECT ContractId,InstallmentNumber,AmountScaled FROM CustomerInstallmentPayments UNION ALL SELECT ContractId,InstallmentNumber,AmountScaled FROM CustomerInstallmentPartialPayments) GROUP BY ContractId,InstallmentNumber) pay ON pay.ContractId=s.ContractId AND pay.InstallmentNumber=s.InstallmentNumber
                    LEFT JOIN (SELECT ContractId,InstallmentNumber,SUM(AmountScaled) AmountScaled FROM CustomerInstallmentReturnAllocations GROUP BY ContractId,InstallmentNumber) red ON red.ContractId=s.ContractId AND red.InstallmentNumber=s.InstallmentNumber
                    LEFT JOIN AlertReadStates rs ON rs.UserId=@userId AND rs.AlertId='installment:'||s.ContractId||':'||s.InstallmentNumber
                    WHERE s.AmountScaled-COALESCE(red.AmountScaled,0)>COALESCE(pay.AmountScaled,0) AND s.DueOn<=@until
                    ORDER BY Date,Id",new {userId,today,until},cancellationToken:ct)).ConfigureAwait(false);
                return rows.ToList();
            }
        }
        public async Task SetReadAsync(long userId,string alertId,bool read,DateTime asOfUtc,CancellationToken ct)
        {
            using(var c=new SQLiteConnection(_connectionString)){await c.OpenAsync(ct).ConfigureAwait(false);
                if(read) await c.ExecuteAsync(new CommandDefinition("INSERT OR REPLACE INTO AlertReadStates(UserId,AlertId,ReadUtc) VALUES(@userId,@alertId,@now)",new {userId,alertId,now=asOfUtc.ToString("o",CultureInfo.InvariantCulture)},cancellationToken:ct)).ConfigureAwait(false);
                else await c.ExecuteAsync(new CommandDefinition("DELETE FROM AlertReadStates WHERE UserId=@userId AND AlertId=@alertId",new {userId,alertId},cancellationToken:ct)).ConfigureAwait(false);
            }
        }
        public async Task MarkAllReadAsync(long userId,DateTime asOfUtc,bool stockOnly,CancellationToken ct)
        {
            var alerts=await ActiveAsync(userId,asOfUtc,ct).ConfigureAwait(false);
            using(var c=new SQLiteConnection(_connectionString)){await c.OpenAsync(ct).ConfigureAwait(false);using(var tx=c.BeginTransaction()){
                foreach(var alert in alerts.Where(x=>!stockOnly || x.Type=="low-stock" || x.Type=="reorder")) await c.ExecuteAsync(new CommandDefinition("INSERT OR REPLACE INTO AlertReadStates(UserId,AlertId,ReadUtc) VALUES(@userId,@id,@now)",new {userId,id=alert.Id,now=asOfUtc.ToString("o",CultureInfo.InvariantCulture)},tx,cancellationToken:ct)).ConfigureAwait(false);
                tx.Commit();
            }}
        }
    }
}
