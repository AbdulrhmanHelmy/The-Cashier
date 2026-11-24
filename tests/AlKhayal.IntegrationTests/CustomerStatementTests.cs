using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Database;
using AlKhayal.Application.Customers;
using AlKhayal.Core.Models;
using AlKhayal.Data.Customers;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class CustomerStatementTests
    {
        [Fact]
        public async Task Paged_ledger_matches_full_period_totals_and_running_balances()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var customer = new CustomerService(new SqliteCustomerRepository(f.ConnectionString))
                    .Create(new Customer { Name = "اختبار كشف الحساب", Phone = "010", Status = "نشط" });
                using (var c = f.Open())
                {
                    c.Execute(@"WITH RECURSIVE Seed(N) AS (SELECT 1 UNION ALL SELECT N+1 FROM Seed WHERE N<10000)
                        INSERT INTO CustomerTransactions(CustomerId,TransactionType,AmountScaled,OccurredOn,CreatedUtc)
                        SELECT @Id, CASE WHEN N%2=0 THEN 'increase' ELSE 'reduction' END, N,
                               CASE WHEN N<100 THEN '2026-01-01' ELSE '2026-01-02' END,'2026-01-02'
                        FROM Seed", new { customer.Id });
                }
                var service = new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                const string date = "2026-01-02";
                var full = service.GetStatement(customer.Id, date, date);
                var page = await service.GetStatementPageAsync(customer.Id, date, date, 3, 25, System.Threading.CancellationToken.None);
                Assert.Equal(full.OpeningBalance, page.OpeningBalance);
                Assert.Equal(full.CurrentBalance, page.CurrentBalance);
                Assert.Equal(full.ClosingBalance, page.ClosingBalance);
                Assert.Equal(full.TotalCredit, page.TotalCredit);
                Assert.Equal(full.TotalDebit, page.TotalDebit);
                Assert.Equal(full.Movements.Count - 1, page.TotalCount);
                Assert.Equal(full.Movements.Skip(51).Take(25).Select(x => (x.TransactionId, x.Balance)),
                    page.Movements.Select(x => (x.TransactionId, x.Balance)));
            }
        }

        [Fact]
        public async Task Directory_search_and_sort_cover_customers_outside_first_page()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                using (var c = f.Open())
                {
                    c.Execute(@"WITH RECURSIVE Seed(N) AS (SELECT 1 UNION ALL SELECT N+1 FROM Seed WHERE N<10000)
                        INSERT INTO Customers(Code,Name,Phone,Address,Status,CreatedAt,UpdatedAt)
                        SELECT printf('QA-%05d',N), 'عميل '||N, '010', CASE WHEN N=10000 THEN 'أَمَانَة % مميزة' ELSE '' END,
                               'نشط','2026-01-01','2026-01-01' FROM Seed");
                }
                var service = new CustomerService(new SqliteCustomerRepository(f.ConnectionString));
                var ct = System.Threading.CancellationToken.None;
                var first = await service.ListPageAsync("", 1, 50, "code", false, ct);
                var reverse = await service.ListPageAsync("", 1, 25, "code", true, ct);
                var matches = await service.ListPageAsync("%", 1, 50, "name", false, ct);
                var normalized = await service.ListPageAsync("امانه", 1, 50, "code", false, ct);
                Assert.Equal("QA-10000", Assert.Single(normalized.Items).Code);
                Assert.Equal(10000, first.TotalCount);
                Assert.Equal(50, first.Items.Count);
                Assert.Equal("QA-00001", first.Items[0].Code);
                Assert.Equal("QA-10000", reverse.Items[0].Code);
                Assert.Equal(1, matches.TotalCount);
                Assert.Equal("QA-10000", Assert.Single(matches.Items).Code);
            }
        }

        [Fact]
        public async Task Collections_reconcile_partial_full_destinations_replay_and_rollback()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customer=new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer{Name="عميل تحصيل",Phone="010",Balance=500m,Status="نشط"});
                using(var c=f.Open())c.Execute("INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled) VALUES(@actor,'standalone','2026-01-01T00:00:00Z',@actor,0)",new{actor=f.UserId});
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                var cash=f.CollectionCashAccount();var other=(await f.Create("FR002-ALT")).Id;
                CustomerStatementTransaction Post(decimal amount,string key,long accountId) => service.PostTransaction(customer.Id,"collection",amount,"2026-11-03",null,key,"cash",accountId,f.UserId);
                var first=Post(200m,"fr002-partial",cash);Assert.False(first.Replayed);
                Assert.True(Post(200m,"fr002-partial",cash).Replayed);
                Assert.Throws<CustomerOperationKeyConflictException>(()=>Post(200m,"fr002-partial",other));
                Post(100m,"fr002-second",other);Post(200m,"fr002-full",cash);
                Assert.Throws<ArgumentException>(()=>Post(0.01m,"fr002-overpay",cash));
                using(var c=f.Open())
                {
                    Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT SUM(CASE WHEN TransactionType='opening' THEN AmountScaled ELSE -AmountScaled END) FROM CustomerTransactions WHERE CustomerId=@Id",new{customer.Id}));
                    Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerCollectionTreasuryLinks"));
                    Assert.Equal(3,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CashierShiftEvents e JOIN CustomerCollectionTreasuryLinks l ON l.TreasuryMovementId=e.TreasuryMovementId WHERE e.Kind='collection'"));
                    Assert.Equal(5000000,c.ExecuteScalar<long>("SELECT SUM(m.AmountScaled) FROM CustomerCollectionTreasuryLinks l JOIN TreasuryMovements m ON m.Id=l.TreasuryMovementId"));
                    Assert.Equal(4000000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM TreasuryMovements WHERE AccountId=@cash",new{cash}));
                    Assert.Equal(1000000,c.ExecuteScalar<long>("SELECT SUM(AmountScaled) FROM TreasuryMovements WHERE AccountId=@other",new{other}));
                }
            }
        }
        [Fact]
        public async Task Missing_inactive_and_incompatible_accounts_cannot_collect_and_failures_roll_back()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customer=new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer{Name="عميل تحقق",Phone="010",Balance=50m,Status="نشط"});
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                var cash=f.CollectionCashAccount();var bank=(await f.Create("FR002-BANK","bank")).Id;
                var inactive=await f.Create("FR002-OFF");await f.Service.DeleteAsync(inactive.Id,inactive.Version,f.UserId,System.Threading.CancellationToken.None);
                Assert.Throws<ArgumentException>(()=>service.PostTransaction(customer.Id,"collection",1m,"2026-11-03",null,"missing"));
                foreach(var id in new[]{999999L,inactive.Id,bank})Assert.Throws<TreasuryException>(()=>service.PostTransaction(customer.Id,"collection",1m,"2026-11-03",null,"bad-"+id,"cash",id,f.UserId));
                using(var c=f.Open())c.Execute("CREATE TRIGGER fail_collection_treasury BEFORE INSERT ON TreasuryMovements BEGIN SELECT RAISE(ABORT,'injected'); END");
                Assert.Throws<SQLiteException>(()=>service.PostTransaction(customer.Id,"collection",5m,"2026-11-03",null,"rollback","cash",cash,f.UserId));
                using(var c=f.Open())
                {
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey='rollback'"));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerCollectionTreasuryLinks"));
                    Assert.Equal(50m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));
                    c.Execute("DROP TRIGGER fail_collection_treasury");
                    c.Execute("CREATE TRIGGER fail_collection_balance BEFORE UPDATE OF Balance ON Customers BEGIN SELECT RAISE(ABORT,'injected late failure'); END");
                }
                Assert.Throws<SQLiteException>(()=>service.PostTransaction(customer.Id,"collection",5m,"2026-11-03",null,"rollback","cash",cash,f.UserId));
                using(var c=f.Open())
                {
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerCollectionTreasuryLinks"));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey='rollback'"));
                    c.Execute("DROP TRIGGER fail_collection_balance");
                }
                Assert.False(service.PostTransaction(customer.Id,"collection",5m,"2026-11-03",null,"rollback","cash",cash,f.UserId).Replayed);
            }
        }
        [Fact]
        public async Task Concurrent_same_key_collection_posts_once_and_historical_rows_are_unchanged()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customer=new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer{Name="عميل تاريخ",Phone="010",Balance=50m,Status="نشط"});
                using(var c=f.Open())
                {
                    c.Execute("INSERT INTO CustomerTransactions(CustomerId,OperationKey,RequestHash,TransactionType,AmountScaled,OccurredOn,Notes,CreatedUtc) VALUES(@Id,'old-collection','old-hash','collection',100000,'2025-01-01','تاريخي','2025-01-01T00:00:00Z')",new{Id=customer.Id});
                    c.Execute("UPDATE Customers SET Balance=40 WHERE Id=@Id",new{customer.Id});
                }
                Migrator.Run(f.ConnectionString);
                var cash=f.CollectionCashAccount();
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                Assert.Throws<CustomerOperationKeyConflictException>(()=>service.PostTransaction(customer.Id,"collection",10m,"2025-01-01","تاريخي","old-collection","cash",cash,f.UserId));
                async Task<Exception> Attempt()
                {
                    try{await Task.Run(()=>service.PostTransaction(customer.Id,"collection",20m,"2026-11-03",null,"concurrent-collection","cash",cash,f.UserId));return null;}
                    catch(Exception ex){return ex;}
                }
                var results=await Task.WhenAll(Attempt(),Attempt());
                Assert.Contains(results,x=>x==null);
                Assert.All(results,x=>Assert.True(x==null||x is SQLiteException,x==null?"":x.ToString()));
                Assert.True(service.PostTransaction(customer.Id,"collection",20m,"2026-11-03",null,"concurrent-collection","cash",cash,f.UserId).Replayed);
                using(var c=f.Open())
                {
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey='concurrent-collection'"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerCollectionTreasuryLinks"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM TreasuryMovements"));
                    Assert.Equal(20m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));
                    Assert.Equal(100000,c.ExecuteScalar<long>("SELECT AmountScaled FROM CustomerTransactions WHERE OperationKey='old-collection'"));
                    Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerCollectionTreasuryLinks WHERE CustomerTransactionId=(SELECT Id FROM CustomerTransactions WHERE OperationKey='old-collection')"));
                }
            }
        }
        [Fact]
        public void Legacy_payment_api_writer_links_one_deposit_and_rejects_changed_retry()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customer=new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer{Name="عميل واجهة قديمة",Phone="010",Balance=30m,Status="نشط"});
                var cash=f.CollectionCashAccount();var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                CustomerPayment Input(decimal amount)=>new CustomerPayment{Amount=amount,PaymentDate="2026-11-03",Method="cash",Account="الخزنة",Notes="تحصيل",OperationKey="legacy-fr002",TreasuryAccountId=cash};
                Assert.Throws<ArgumentException>(()=>service.AddPayment(customer.Id,new CustomerPayment{Amount=1m,PaymentDate="2026-11-03",Method="cash",Account="الخزنة"},f.UserId));
                var first=service.AddPayment(customer.Id,Input(10m),f.UserId);
                Assert.Equal(first.Id,service.AddPayment(customer.Id,Input(10m),f.UserId).Id);
                Assert.Throws<CustomerOperationKeyConflictException>(()=>service.AddPayment(customer.Id,Input(11m),f.UserId));
                using(var c=f.Open())
                {
                    Assert.Equal(20m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerPayments WHERE OperationKey='legacy-fr002'"));
                    Assert.Equal(1,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerCollectionTreasuryLinks l JOIN CustomerTransactions t ON t.Id=l.CustomerTransactionId JOIN TreasuryMovements m ON m.Id=l.TreasuryMovementId WHERE t.CustomerId=@Id AND m.AccountId=@cash AND m.AmountScaled=100000",new{Id=customer.Id,cash}));
                }
            }
        }
        [Fact]
        public void All_time_and_filtered_statements_reconcile_once_and_retain_current_balance_notes_and_restart()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customers=new CustomerService(new SqliteCustomerRepository(f.ConnectionString));
                var customer=customers.Create(new Customer{Name="عميل كشف",Phone="010",Balance=100,Status="نشط"});
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                service.PostTransaction(customer.Id,"increase",50.50m,"2026-10-28","مديونية جديدة","statement-increase");
                service.PostTransaction(customer.Id,"collection",20.25m,"2026-10-28","تحصيل جزئي","statement-collection","cash",CollectionTestAccounts.Account(f.ConnectionString),CollectionTestAccounts.Actor(f.ConnectionString));
                var statement=service.GetStatement(customer.Id,null,null);
                Assert.Equal(0m,statement.OpeningBalance);Assert.Equal(150.50m,statement.TotalDebit);Assert.Equal(20.25m,statement.TotalCredit);
                Assert.Equal(130.25m,statement.ClosingBalance);Assert.Equal(130.25m,statement.CurrentBalance);Assert.Equal(statement.ClosingBalance,statement.Movements.Last().Balance);
                Assert.Contains(statement.Movements,x=>x.Notes=="مديونية جديدة");Assert.Contains(statement.Movements,x=>x.Notes=="تحصيل جزئي");
                // Creation records an opening entry on the actual UTC day; derive filter boundaries from the fixture.
                string afterDate, beforeDate; using(var c=f.Open()){afterDate=DateTime.Parse(c.ExecuteScalar<string>("SELECT MAX(OccurredOn) FROM CustomerTransactions WHERE CustomerId=@Id",new{customer.Id}),System.Globalization.CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd");beforeDate=DateTime.Parse(c.ExecuteScalar<string>("SELECT MIN(OccurredOn) FROM CustomerTransactions WHERE CustomerId=@Id",new{customer.Id}),System.Globalization.CultureInfo.InvariantCulture).AddDays(-1).ToString("yyyy-MM-dd");}
                var later=service.GetStatement(customer.Id,afterDate,afterDate);Assert.Equal(130.25m,later.OpeningBalance);Assert.Equal(130.25m,later.ClosingBalance);Assert.Equal(130.25m,later.CurrentBalance);Assert.Equal(0m,later.TotalDebit);
                var before=service.GetStatement(customer.Id,null,beforeDate);Assert.Equal(0m,before.ClosingBalance);Assert.Equal(130.25m,before.CurrentBalance);
                Assert.True(service.PostTransaction(customer.Id,"collection",20.25m,"2026-10-28","تحصيل جزئي","statement-collection","cash",CollectionTestAccounts.Account(f.ConnectionString),CollectionTestAccounts.Actor(f.ConnectionString)).Replayed);
                Assert.Equal(130.25m,new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString)).GetStatement(customer.Id,null,null).ClosingBalance);
                Assert.Throws<ArgumentException>(()=>service.PostTransaction(customer.Id,"collection",130.26m,"2026-10-28",null,"over-collection","cash",CollectionTestAccounts.Account(f.ConnectionString),CollectionTestAccounts.Actor(f.ConnectionString)));
            }
        }
        [Fact]
        public void Failed_balance_update_rolls_back_entry_and_retry_identity_then_increase_can_exceed_existing_debt()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customer=new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer{Name="عميل تراجع",Phone="010",Status="نشط"});
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                using(var c=f.Open())c.Execute("CREATE TRIGGER fail_customer_statement BEFORE UPDATE OF Balance ON Customers BEGIN SELECT RAISE(ABORT,'injected failure'); END");
                Assert.Throws<SQLiteException>(()=>service.PostTransaction(customer.Id,"increase",200.10m,"2026-10-28",null,"late-customer-failure"));
                using(var c=f.Open()){Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey='late-customer-failure'"));Assert.Equal(0m,c.ExecuteScalar<decimal>("SELECT Balance FROM Customers WHERE Id=@Id",new{customer.Id}));c.Execute("DROP TRIGGER fail_customer_statement");}
                Assert.False(service.PostTransaction(customer.Id,"increase",200.10m,"2026-10-28",null,"late-customer-failure").Replayed);
                Assert.True(service.PostTransaction(customer.Id,"increase",200.10m,"2026-10-28",null,"late-customer-failure").Replayed);
                Assert.Equal(200.10m,service.GetStatement(customer.Id,null,null).CurrentBalance);
            }
        }
        [Fact]
        public void Overflow_is_rejected_before_an_entry_is_inserted()
        {
            using(var f=new TreasuryPersistenceTests.Fixture())
            {
                var customer=new CustomerService(new SqliteCustomerRepository(f.ConnectionString)).Create(new Customer{Name="عميل الحدود",Phone="010",Status="نشط"});
                var service=new CustomerAccountService(new SqliteCustomerAccountRepository(f.ConnectionString));
                service.PostTransaction(customer.Id,"increase",922337203685477.58m,"2026-10-28",null,"max-debt");
                Assert.Throws<OverflowException>(()=>service.PostTransaction(customer.Id,"increase",0.01m,"2026-10-28",null,"too-much-debt"));
                using(var c=f.Open())Assert.Equal(0,c.ExecuteScalar<long>("SELECT COUNT(*) FROM CustomerTransactions WHERE OperationKey='too-much-debt'"));
                Assert.Equal(922337203685477.58m,service.GetStatement(customer.Id,null,null).CurrentBalance);
            }
        }
    }
}
