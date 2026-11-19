using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Exports;
using AlKhayal.Data.Database;
using AlKhayal.Data.Exports;
using Dapper;
using Xunit;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using AlKhayal.Application.Authentication;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using AlKhayal.Core.Models;
using AlKhayal.Application.Users;

namespace AlKhayal.IntegrationTests
{
    public sealed class ExportPersistenceTests
    {
        [Fact] public async Task Filters_query_all_matching_rows_and_project_only_business_columns()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");try{var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();c.Execute("INSERT INTO Customers(Code,Name,Phone,Address,Balance,CreditLimit,Subscription,Status,CreatedAt,UpdatedAt) VALUES('C1','أحمد','00123','القاهرة',15.25,0,'أساسي','نشط','2026','2026'),('C2','منى','00999','الجيزة',0,0,'أساسي','غير نشط','2026','2026'); INSERT INTO Suppliers(Code,Name,Contact,Phone,Address,Payable,SupplierCredit,Status,CreatedAt,UpdatedAt) VALUES('S1','مورد ألف','', '0007','عنوان',100,25,'نشط','2026','2026');");}
                var source=new SqliteExportDataSource(cs);var customers=await source.QueryAsync(new ExportQuery{Dataset="customers",Filters=new Dictionary<string,string>{{"search","أح"},{"status","نشط"}}},CancellationToken.None);Assert.Single(customers);Assert.Equal(new[]{"name","phone","address","balance","status"},customers[0].Values.Keys);Assert.Equal("00123",customers[0].Values["phone"]);var suppliers=await source.QueryAsync(new ExportQuery{Dataset="suppliers",Filters=new Dictionary<string,string>()},CancellationToken.None);Assert.Equal(75m,Convert.ToDecimal(suppliers.Single().Values["balance"]));
            }finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact] public async Task Product_export_is_unpaged_stable_and_handles_fifteen_thousand_rows()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");try{var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);using(var c=new SQLiteConnection(cs)){c.Open();using(var tx=c.BeginTransaction()){for(var i=1;i<=15000;i++){var id=c.ExecuteScalar<long>("INSERT INTO Products(Name,Price) VALUES(@Name,0);SELECT last_insert_rowid();",new{Name="منتج "+i.ToString("D5")},tx);c.Execute("INSERT INTO ProductDetails(ProductId,Description,CategoryId,BaseUnitId,ProductType,SellPriceScaled,PurchasePriceScaled,IsActive,ArchivedUtc,MigrationState,Version,CreatedUtc,UpdatedUtc) VALUES(@Id,'',NULL,1,'stock',125000,100000,1,NULL,'ready',1,'2026','2026')",new{Id=id},tx);}tx.Commit();}}
                var rows=await new SqliteExportDataSource(cs).QueryAsync(new ExportQuery{Dataset="products",Filters=new Dictionary<string,string>{{"search","منتج"},{"isActive","true"}}},CancellationToken.None);Assert.Equal(15000,rows.Count);Assert.Equal("منتج 00001",rows[0].Values["name"]);Assert.Equal("منتج 15000",rows[14999].Values["name"]);Assert.All(rows,x=>Assert.Equal(8,x.Values.Count));
            }finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact] public async Task Invalid_filters_are_rejected_before_sql_and_database_is_read_only()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");try{var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var before=File.ReadAllBytes(path);var source=new SqliteExportDataSource(cs);await Assert.ThrowsAsync<ArgumentException>(()=>source.QueryAsync(new ExportQuery{Dataset="sales",Filters=new Dictionary<string,string>{{"from","not-a-date"}}},CancellationToken.None));await source.QueryAsync(new ExportQuery{Dataset="warehouses",Filters=new Dictionary<string,string>()},CancellationToken.None);SQLiteConnection.ClearAllPools();Assert.Equal(before,File.ReadAllBytes(path));}finally{SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }

        [Fact] public async Task Http_export_requires_authentication_and_rejects_invalid_dataset_format_and_filter()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");AlKhayalServer server=null;try{var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);await new SqliteUserStore(cs).CreateAsync("export-admin","مسؤول",new PasswordHasher().Hash("secret"),UserRoles.Administrator,CancellationToken.None);var url="http://127.0.0.1:"+FreePort();server=new AlKhayalServer(new TestLicenseGate());server.Start(new HostOptions{DatabasePath=path,Url=url,FrontendPath=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))});using(var client=new HttpClient(new HttpClientHandler{UseCookies=true}){BaseAddress=new Uri(url)}){Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("api/exports/customers/csv")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("api/auth/login",new StringContent("{\"username\":\"export-admin\",\"password\":\"secret\"}",Encoding.UTF8,"application/json"))).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("api/exports/users/csv")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("api/exports/customers/pdf")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("api/exports/customers/csv?password=x")).StatusCode);var valid=await client.GetAsync("api/exports/customers/csv?search=none");Assert.Equal(HttpStatusCode.OK,valid.StatusCode);Assert.Equal("text/csv",valid.Content.Headers.ContentType.MediaType);Assert.Equal(new byte[]{0xEF,0xBB,0xBF},(await valid.Content.ReadAsByteArrayAsync()).Take(3));}}
            finally{server?.Dispose();SQLiteConnection.ClearAllPools();if(File.Exists(path))File.Delete(path);}
        }
        private static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    }
}
