using System;
using System.Data.SQLite;
using Dapper;

namespace AlKhayal.IntegrationTests
{
    internal static class CollectionTestAccounts
    {
        internal static long Actor(string connectionString)
        {
            using(var c=new SQLiteConnection(connectionString))
            {
                c.Open();var id=c.ExecuteScalar<long?>("SELECT Id FROM Users ORDER BY Id LIMIT 1");
                if(id.HasValue)return id.Value;
                return c.ExecuteScalar<long>("INSERT INTO Users(Username,PasswordHash,CreatedUtc) VALUES('fr002-fixture','test-only',@now); SELECT last_insert_rowid();",new{now=DateTime.UtcNow.ToString("o")});
            }
        }
        internal static long Account(string connectionString,string method="cash")
        {
            var type=method=="cash"?"cash":"bank";
            var code=type=="cash"?"FR002-TEST-CASH":"FR002-TEST-BANK";
            using(var c=new SQLiteConnection(connectionString))
            {
                c.Open();var id=c.ExecuteScalar<long?>("SELECT Id FROM TreasuryAccounts WHERE Code=@code AND IsDeleted=0",new{code});
                if(id.HasValue)return id.Value;
                Actor(connectionString);
                return c.ExecuteScalar<long>("INSERT INTO TreasuryAccounts(Code,NormalizedCode,Name,Type,Notes,CreatedUtc,UpdatedUtc) VALUES(@code,@code,'حساب اختبار التحصيل',@type,'',@now,@now); SELECT last_insert_rowid();",new{code,type,now=DateTime.UtcNow.ToString("o")});
            }
        }
    }
}
