using System;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Finance;
using AlKhayal.Application.Sales;
using AlKhayal.Data.Customers;
using Dapper;

namespace AlKhayal.Data.Sales
{
    public sealed partial class SqliteSalesStore
    {
        public async Task<InstallmentSaleResult> CreateInstallmentAsync(InstallmentSaleCommand command,long subtotal,long discount,long downPayment,long userId,CancellationToken ct)
        {
            var hash=InstallmentHash(command,userId);
            try
            {
                long invoiceId,contractId;
                string contractNumber;
                using(var c=Open())using(var tx=c.BeginTransaction())
                {
                    var existing=await c.QuerySingleOrDefaultAsync<InstallmentReplay>(Cmd("SELECT Id,SalesInvoiceId,ContractNumber,RequestHash FROM CustomerInstallmentContracts WHERE OperationKey=@Key",new{Key=command.OperationKey},tx,ct)).ConfigureAwait(false);
                    if(existing!=null)
                    {
                        if(existing.RequestHash!=hash||!existing.SalesInvoiceId.HasValue)return InstallmentSaleResult.Fail("operation_key_conflict","مفتاح العملية مستخدم لعقد مختلف.");
                        tx.Commit();
                        return new InstallmentSaleResult{Succeeded=true,Replayed=true,ContractId=existing.Id,ContractNumber=existing.ContractNumber,Invoice=await GetAsync(existing.SalesInvoiceId.Value,ct).ConfigureAwait(false)};
                    }
                    if(await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM SalesInvoices WHERE OperationKey=@Key",new{Key=command.OperationKey},tx,ct)).ConfigureAwait(false)>0)
                        return InstallmentSaleResult.Fail("operation_key_conflict","مفتاح العملية مستخدم لفاتورة مختلفة.");
                    var sale=command.Invoice;
                    subtotal=await ValidateReferences(c,tx,sale,discount,ct).ConfigureAwait(false);
                    var taxSettings=await TaxAsync(c,tx,ct).ConfigureAwait(false);
                    var totals=Totals(subtotal,discount,taxSettings.Rate,taxSettings.Enabled,taxSettings.Included);
                    var total=checked(totals.total+(sale.Delivery==null?0:sale.Delivery.FeeScaled));
                    var interestRate=decimal.Parse(command.InterestRate,CultureInfo.InvariantCulture);
                    var interestAmount=MoneyPolicy.RoundInternalToPiastre(checked((long)decimal.Round((total-downPayment)*(interestRate/100m),0,MidpointRounding.AwayFromZero)));
                    var contractTotal=checked(total+interestAmount);
                    if(downPayment>=total||contractTotal-downPayment<command.InstallmentCount*10000L)
                        return InstallmentSaleResult.Fail("validation","المقدم أو عدد الأقساط لا يناسب إجمالي الفاتورة.");
                    var now=Now();var number=NextNumber();
                    await c.ExecuteAsync(Cmd(@"INSERT INTO SalesInvoices(InvoiceNumber,InvoiceDate,CustomerId,WarehouseId,CashierUserId,Status,PaymentMode,SubtotalScaled,DiscountScaled,TaxScaled,TotalScaled,PaidScaled,TaxRate,TaxIncluded,CreatedUtc)
                        VALUES(@Number,@Date,@CustomerId,@WarehouseId,@UserId,'draft','credit',@Subtotal,@Discount,@Tax,@Total,0,@Rate,@Included,@Now)",new{Number=number,Date=sale.InvoiceDate,sale.CustomerId,sale.WarehouseId,UserId=userId,Subtotal=subtotal,Discount=discount,Tax=totals.tax,Total=total,Rate=taxSettings.Rate,Included=taxSettings.Included?1:0,Now=now},tx,ct)).ConfigureAwait(false);
                    invoiceId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    await SaveSelectionAsync(c,tx,invoiceId,sale,ct).ConfigureAwait(false);
                    await SaveDeliveryAsync(c,tx,invoiceId,sale,ct).ConfigureAwait(false);
                    await SaveLinesAsync(c,tx,invoiceId,sale.Lines,ct).ConfigureAwait(false);
                    await FinalizeLinesAsync(c,tx,invoiceId,discount,totals.tax,taxSettings.Included,ct).ConfigureAwait(false);
                    var posted=await PostWithinTransactionAsync(c,tx,invoiceId,1,command.OperationKey,hash,userId,ct,downPayment).ConfigureAwait(false);
                    if(!posted.Succeeded)return InstallmentSaleResult.Fail(posted.ErrorCode,posted.ErrorMessage);
                    var product=await c.QuerySingleAsync<InstallmentProduct>(Cmd("SELECT p.Id,p.Name FROM ProductVariants v JOIN Products p ON p.Id=v.ProductId WHERE v.Id=@Variant",new{Variant=sale.Lines[0].VariantId},tx,ct)).ConfigureAwait(false);
                    contractNumber="INS-"+Guid.NewGuid().ToString("N").Substring(0,12).ToUpperInvariant();
                    await c.ExecuteAsync(Cmd(@"INSERT INTO CustomerInstallmentContracts(CustomerId,ProductId,SalesInvoiceId,OperationKey,RequestHash,ContractNumber,ProductName,ProductPriceScaled,DownPaymentScaled,InterestRateBasisPoints,TotalAmountScaled,InstallmentCount,IntervalMonths,FirstDueOn,CreatedUtc,InterestReturnPolicy)
                        VALUES(@CustomerId,@ProductId,@InvoiceId,@Key,@Hash,@ContractNumber,@ProductName,@Total,@Down,@Rate,@ContractTotal,@Count,@Interval,@FirstDue,@Now,@InterestReturnPolicy)",new{sale.CustomerId,ProductId=product.Id,InvoiceId=invoiceId,Key=command.OperationKey,Hash=hash,ContractNumber=contractNumber,ProductName=product.Name,Total=total,Down=downPayment,Rate=checked((long)(interestRate*100m)),ContractTotal=contractTotal,Count=command.InstallmentCount,Interval=command.IntervalMonths,FirstDue=command.FirstDueOn,Now=now,command.InterestReturnPolicy},tx,ct)).ConfigureAwait(false);
                    contractId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd(@"INSERT INTO InstallmentReceivableEntries(ContractId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
                        VALUES(@ContractId,'contract_created','debit',@Amount,@Key,@Hash,@Now)",new{ContractId=contractId,Amount=contractTotal,Key="installment-receivable:contract:"+command.OperationKey,Hash=hash,Now=now},tx,ct)).ConfigureAwait(false);
                    if(downPayment>0)await c.ExecuteAsync(Cmd(@"INSERT INTO InstallmentReceivableEntries(ContractId,EntryType,Direction,AmountScaled,OperationKey,RequestHash,CreatedUtc)
                        VALUES(@ContractId,'down_payment','credit',@Amount,@Key,@Hash,@Now)",new{ContractId=contractId,Amount=downPayment,Key="installment-receivable:down:"+command.OperationKey,Hash=hash,Now=now},tx,ct)).ConfigureAwait(false);
                    long cents=checked((contractTotal-downPayment)/100L),each=cents/command.InstallmentCount,remainder=cents%command.InstallmentCount;
                    var first=DateTime.ParseExact(command.FirstDueOn,"yyyy-MM-dd",CultureInfo.InvariantCulture);
                    for(var i=0;i<command.InstallmentCount;i++)
                    {
                        var due=first.AddMonths(checked(i*command.IntervalMonths)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                        await c.ExecuteAsync(Cmd("INSERT INTO CustomerInstallmentSchedule(ContractId,InstallmentNumber,DueOn,AmountScaled) VALUES(@ContractId,@Number,@Due,@Amount)",new{ContractId=contractId,Number=i+1,Due=due,Amount=(each+(i==command.InstallmentCount-1?remainder:0))*100L},tx,ct)).ConfigureAwait(false);
                    }
                    if(interestAmount>0)
                    {
                        await c.ExecuteAsync(Cmd("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference) VALUES(@CustomerId,@Key,@Hash,'increase',@Amount,@Date,@Notes,@Now,@Reference)",new{sale.CustomerId,Key="installment-contract:"+contractId+":interest",Hash=hash,Amount=interestAmount,Date=sale.InvoiceDate,Notes="فائدة عقد تقسيط "+contractNumber,Now=now,Reference=contractNumber},tx,ct)).ConfigureAwait(false);
                        var interestBalanceUpdate=await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=Balance+@Amount,UpdatedAt=@Now WHERE Id=@CustomerId AND (CreditLimit<=0 OR Balance+@Amount<=CreditLimit)",new{Amount=interestAmount/10000m,Now=now,sale.CustomerId},tx,ct)).ConfigureAwait(false);
                        if(interestBalanceUpdate!=1)return InstallmentSaleResult.Fail("credit_limit","تتجاوز الفائدة حد ائتمان العميل.");
                    }
                    if(downPayment>0)
                    {
                        await c.ExecuteAsync(Cmd("INSERT INTO SalesPayments(SalesInvoiceId,CustomerId,ActorUserId,EventType,PaymentMethod,AmountScaled,OperationKey,RequestHash,OccurredOn,CreatedUtc) VALUES(@InvoiceId,@CustomerId,@Actor,'customer_collection','cash',@Amount,@Key,@Hash,@Date,@Now)",new{InvoiceId=invoiceId,sale.CustomerId,Actor=userId,Amount=downPayment,Key="installment-down:"+command.OperationKey,Hash=hash,Date=sale.InvoiceDate,Now=now},tx,ct)).ConfigureAwait(false);
                        var paymentId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                        await c.ExecuteAsync(Cmd("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc,Reference,SalesInvoiceId) VALUES(@CustomerId,@Key,@Hash,'collection',@Amount,@Date,@Notes,@Now,@Reference,@InvoiceId)",new{sale.CustomerId,Key="installment-contract:"+contractId+":down-payment",Hash=hash,Amount=downPayment,Date=sale.InvoiceDate,Notes="مقدم بيع بالتقسيط",Now=now,Reference=contractNumber,InvoiceId=invoiceId},tx,ct)).ConfigureAwait(false);
                        var ledgerId=await c.ExecuteScalarAsync<long>(Cmd("SELECT last_insert_rowid()",null,tx,ct)).ConfigureAwait(false);
                        var movement=await CustomerCollectionTreasury.PostAsync(c,tx,ledgerId,command.TreasuryAccountId,"cash",downPayment,hash,userId,ct).ConfigureAwait(false);
                        await c.ExecuteAsync(Cmd("UPDATE Customers SET Balance=Balance-@Amount,UpdatedAt=@Now WHERE Id=@CustomerId",new{Amount=downPayment/10000m,Now=now,sale.CustomerId},tx,ct)).ConfigureAwait(false);
                        await c.ExecuteAsync(Cmd("UPDATE SalesInvoices SET PaidScaled=@Amount WHERE Id=@InvoiceId",new{Amount=downPayment,InvoiceId=invoiceId},tx,ct)).ConfigureAwait(false);
                        await AlKhayal.Data.Workforce.SqliteWorkforceStore.AssociateAsync(c,tx,userId,"collection",downPayment,invoiceId,paymentId,ct,movement).ConfigureAwait(false);
                    }
                    tx.Commit();
                }
                return new InstallmentSaleResult{Succeeded=true,ContractId=contractId,ContractNumber=contractNumber,Invoice=await GetAsync(invoiceId,ct).ConfigureAwait(false)};
            }
            catch(ArgumentException e){return InstallmentSaleResult.Fail("validation",e.Message);}
            catch(AlKhayal.Application.Treasury.TreasuryException e){return InstallmentSaleResult.Fail("treasury_"+e.Code,e.Message);}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Busy||e.ResultCode==SQLiteErrorCode.Locked){return InstallmentSaleResult.Fail("database_busy","قاعدة البيانات مشغولة. أعد المحاولة بالمفتاح نفسه.");}
            catch(SQLiteException e) when(e.ResultCode==SQLiteErrorCode.Constraint){return InstallmentSaleResult.Fail("sales_conflict","تعارضت بيانات البيع مع عملية أخرى. أعد المحاولة بالمفتاح نفسه.");}
            catch(OverflowException){return InstallmentSaleResult.Fail("numeric_overflow","قيمة البيع تتجاوز الحد المسموح.");}
        }
        private static string InstallmentHash(InstallmentSaleCommand command,long actor)
        {
            var sale=command.Invoice;var legacyTerms=command.InterestRate=="0.00"&&command.InterestReturnPolicy=="keep";var text=new StringBuilder(legacyTerms?"installment-sale-v1|":"installment-sale-v2|").Append(actor).Append('|').Append(sale.CustomerId).Append('|').Append(sale.WarehouseId).Append('|').Append(sale.InvoiceDate).Append('|').Append(sale.PaymentMode).Append('|').Append(sale.ImmediateMethod).Append('|').Append(sale.Discount).Append('|').Append(command.DownPayment);
            if(!legacyTerms)text.Append('|').Append(command.InterestRate).Append('|').Append(command.InterestReturnPolicy);
            text.Append('|').Append(command.InstallmentCount).Append('|').Append(command.IntervalMonths).Append('|').Append(command.FirstDueOn).Append('|').Append(command.TreasuryAccountId);
            if(sale.Delivery!=null)text.Append("|delivery:").Append(sale.Delivery.DriverId).Append(':').Append(sale.Delivery.PrimaryPhone).Append(':').Append(sale.Delivery.SecondaryPhone).Append(':').Append(sale.Delivery.Address).Append(':').Append(sale.Delivery.Fee).Append(':').Append(sale.Delivery.Notes);
            foreach(var line in sale.Lines)
            {
                text.Append('|').Append(line.VariantId).Append(':').Append(line.ProductUnitId).Append(':').Append(line.Quantity).Append(':').Append(line.UnitPrice).Append(':').Append(line.ManualPriceOverride).Append(':').Append(line.Discount).Append(':').Append(line.BatchLotNumber).Append(':').Append(line.ExpirationDate);
                foreach(var unit in line.PhysicalUnits??new System.Collections.Generic.List<SaleTrackingUnit>())text.Append(':').Append(unit.Serial).Append('=').Append(string.Join(",",unit.Imeis??new System.Collections.Generic.List<string>()));
            }
            using(var sha=SHA256.Create())return "v1:"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
        }
        private sealed class InstallmentReplay { public long Id { get; set; } public long? SalesInvoiceId { get; set; } public string ContractNumber { get; set; } public string RequestHash { get; set; } }
        private sealed class InstallmentProduct { public long Id { get; set; } public string Name { get; set; } }
    }
}
