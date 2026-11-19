using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Features;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Data.Database;
using AlKhayal.Data.Users;
using AlKhayal.Server.Host;
using Dapper;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class UserManagementPersistenceTests
    {
        [Fact]
        public async Task Users_permissions_last_admin_and_rollback_are_persisted_transactionally()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=Db.ConnectionStringFor(path); Migrator.Run(cs); Migrator.Run(cs);
                var authUsers=new SqliteUserStore(cs); var hasher=new PasswordHasher();
                var originalHash=hasher.Hash("admin-secret"); var adminId=await authUsers.CreateAsync("owner","المالك",originalHash,UserRoles.Administrator,CancellationToken.None);
                var store=new SqliteUserManagementStore(cs); var service=new UserManagementService(store,new Catalog(),hasher);
                var secondAdmin=await service.CreateAsync(new CreateUserCommand{Username="admin2",DisplayName="مسؤول ثان",Password="secret",Role=UserRoles.Administrator,IsActive=true,ActingUserId=adminId},CancellationToken.None);
                var first=await service.CreateAsync(new CreateUserCommand{Username="cashier1",DisplayName="كاشير أول",Password="secret",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None);
                var second=await service.CreateAsync(new CreateUserCommand{Username="cashier2",DisplayName="كاشير ثان",Password="secret",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None);
                Assert.True(secondAdmin.Succeeded); Assert.True(first.Succeeded); Assert.True(second.Succeeded);
                var cashierIdentity=new AlKhayal.Application.Authentication.UserIdentity{Id=first.Value.Id,Role=UserRoles.Cashier};
                var adminIdentity=new AlKhayal.Application.Authentication.UserIdentity{Id=adminId,Role=UserRoles.Administrator};
                Assert.False(await service.CanEditSalePricesAsync(cashierIdentity,CancellationToken.None));
                Assert.True(await service.CanEditSalePricesAsync(adminIdentity,CancellationToken.None));
                Assert.False(await service.CanViewPosStockQuantitiesAsync(cashierIdentity,CancellationToken.None));
                Assert.True(await service.CanViewPosStockQuantitiesAsync(adminIdentity,CancellationToken.None));
                Assert.True((await service.SetPosStockPermissionAsync(first.Value.Id,true,adminId,CancellationToken.None)).Succeeded);
                Assert.True(await new UserManagementService(new SqliteUserManagementStore(cs),new Catalog(),hasher).CanViewPosStockQuantitiesAsync(cashierIdentity,CancellationToken.None));
                Assert.True((await service.SetPosStockPermissionAsync(first.Value.Id,false,adminId,CancellationToken.None)).Succeeded);
                Assert.False(await service.CanViewPosStockQuantitiesAsync(cashierIdentity,CancellationToken.None));
                Assert.True((await service.SetSalePricePermissionAsync(first.Value.Id,true,adminId,CancellationToken.None)).Succeeded);
                Assert.True(await new UserManagementService(new SqliteUserManagementStore(cs),new Catalog(),hasher).CanEditSalePricesAsync(cashierIdentity,CancellationToken.None));
                Assert.True((await service.SetSalePricePermissionAsync(first.Value.Id,false,adminId,CancellationToken.None)).Succeeded);
                Assert.False(await service.CanEditSalePricesAsync(cashierIdentity,CancellationToken.None));
                Assert.Equal("duplicate_username",(await service.CreateAsync(new CreateUserCommand{Username="CASHIER1",DisplayName="مكرر",Password="x",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.Equal("validation",(await service.CreateAsync(new CreateUserCommand{Username="bad",DisplayName="غير صالح",Password="x",Role="manager",IsActive=true,ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.Equal("invalid_screen",(await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=first.Value.Id,ScreenIds=new[]{"missing"},ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.Equal("administrator_only_screen",(await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=first.Value.Id,ScreenIds=new[]{"users-list"},ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.True((await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=first.Value.Id,ScreenIds=new[]{"dashboard","pos"},ActingUserId=adminId},CancellationToken.None)).Succeeded);
                Assert.True((await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=second.Value.Id,ScreenIds=new[]{"sales-list"},ActingUserId=adminId},CancellationToken.None)).Succeeded);
                Assert.Equal(new[]{"dashboard","pos"},await new SqliteUserManagementStore(cs).GetScreenIdsAsync(first.Value.Id,CancellationToken.None));
                Assert.Equal(new[]{"sales-list"},await new SqliteUserManagementStore(cs).GetScreenIdsAsync(second.Value.Id,CancellationToken.None));
                await service.SetStatusAsync(new SetUserStatusCommand{UserId=secondAdmin.Value.Id,IsActive=false,ActingUserId=adminId},CancellationToken.None);
                Assert.Equal("last_active_administrator",(await service.SetStatusAsync(new SetUserStatusCommand{UserId=adminId,IsActive=false,ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                Assert.Equal("last_active_administrator",(await service.UpdateAsync(new UpdateUserCommand{UserId=adminId,Username="owner",DisplayName="المالك",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None)).ErrorCode);
                await Assert.ThrowsAsync<SQLiteException>(()=>service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=first.Value.Id,ScreenIds=new[]{"sales-list"},ActingUserId=999999},CancellationToken.None));
                Assert.Equal(new[]{"dashboard","pos"},await store.GetScreenIdsAsync(first.Value.Id,CancellationToken.None));
                using(var connection=new SQLiteConnection(cs)){var hash=connection.ExecuteScalar<string>("SELECT PasswordHash FROM Users WHERE Id=@Id",new{Id=adminId});Assert.Equal(originalHash,hash);Assert.True(connection.ExecuteScalar<long>("SELECT COUNT(*) FROM UserAuditLog")>=5);}
            }
            finally { SQLiteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
        }

        [Fact]
        public async Task Effective_access_combines_role_grants_and_enabled_modules()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var hasher=new PasswordHasher();var authUsers=new SqliteUserStore(cs);var admin=await authUsers.CreateAsync("admin","مسؤول",hasher.Hash("x"),UserRoles.Administrator,CancellationToken.None);var store=new SqliteUserManagementStore(cs);var service=new UserManagementService(store,new Catalog(),hasher);var cashier=(await service.CreateAsync(new CreateUserCommand{Username="cashier",DisplayName="كاشير",Password="x",Role=UserRoles.Cashier,IsActive=true,ActingUserId=admin},CancellationToken.None)).Value;await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=cashier.Id,ScreenIds=new[]{"dashboard","pos","sales-list"},ActingUserId=admin},CancellationToken.None);
                var features=new FeatureSnapshot{Modules=new[]{new EffectiveFeature{Key="sales",IsEnabled=true},new EffectiveFeature{Key="pos",IsEnabled=false}},Capabilities=new EffectiveFeature[0]};
                var access=await service.GetEffectiveAccessAsync(new AlKhayal.Application.Authentication.UserIdentity{Id=cashier.Id,Role=UserRoles.Cashier},features,CancellationToken.None);Assert.Contains("dashboard",access.ScreenIds);Assert.Contains("sales-list",access.ScreenIds);Assert.DoesNotContain("pos",access.ScreenIds);Assert.DoesNotContain("users-list",access.ScreenIds);
                features.Modules=new[]{new EffectiveFeature{Key="sales",IsEnabled=true},new EffectiveFeature{Key="pos",IsEnabled=true}};access=await service.GetEffectiveAccessAsync(new AlKhayal.Application.Authentication.UserIdentity{Id=cashier.Id,Role=UserRoles.Cashier},features,CancellationToken.None);Assert.Contains("pos",access.ScreenIds);
            }
            finally { SQLiteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
        }

        [Fact]
        public async Task Permission_catalog_excludes_obsolete_routes_and_preserves_explicit_alias_grants()
        {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
            try
            {
                var cs=Db.ConnectionStringFor(path);Migrator.Run(cs);var hasher=new PasswordHasher();var authUsers=new SqliteUserStore(cs);var adminId=await authUsers.CreateAsync("admin","مسؤول",hasher.Hash("x"),UserRoles.Administrator,CancellationToken.None);var store=new SqliteUserManagementStore(cs);var catalog=new PermissionCatalog();var service=new UserManagementService(store,catalog,hasher);var cashier=(await service.CreateAsync(new CreateUserCommand{Username="cashier",DisplayName="كاشير",Password="x",Role=UserRoles.Cashier,IsActive=true,ActingUserId=adminId},CancellationToken.None)).Value;

                await store.SetScreenIdsAsync(new SetScreenPermissionsCommand{UserId=cashier.Id,ScreenIds=new[]{"dashboard","return-details","legacy-sales"},ActingUserId=adminId},CancellationToken.None);
                var features=new FeatureSnapshot{Modules=new[]{new EffectiveFeature{Key="returns",IsEnabled=false}},Capabilities=new EffectiveFeature[0]};
                var listed=await service.GetPermissionsAsync(cashier.Id,features,CancellationToken.None);
                Assert.True(listed.Succeeded);
                Assert.Equal(new[]{"الرئيسية","نظرة عامة على المرتجعات","قائمة المبيعات","شاشة جديدة"},listed.Value.Select(item=>item.ArabicName).ToArray());
                Assert.DoesNotContain(listed.Value,item=>item.ScreenId=="return-details"||item.ScreenId=="legacy-sales"||item.ScreenId=="coming-soon");
                Assert.True(listed.Value.Single(item=>item.ScreenId=="returns-overview").IsGranted);
                Assert.False(listed.Value.Single(item=>item.ScreenId=="returns-overview").IsEffective);
                Assert.False(listed.Value.Single(item=>item.ScreenId=="new-screen").IsGranted);

                var effective=await service.GetEffectiveAccessAsync(new AlKhayal.Application.Authentication.UserIdentity{Id=cashier.Id,Role=UserRoles.Cashier},features,CancellationToken.None);
                Assert.Contains("dashboard",effective.ScreenIds);Assert.DoesNotContain("returns-overview",effective.ScreenIds);Assert.DoesNotContain("sales-list",effective.ScreenIds);
                features.Modules=new[]{new EffectiveFeature{Key="returns",IsEnabled=true}};
                effective=await service.GetEffectiveAccessAsync(new AlKhayal.Application.Authentication.UserIdentity{Id=cashier.Id,Role=UserRoles.Cashier},features,CancellationToken.None);
                Assert.Contains("returns-overview",effective.ScreenIds);Assert.DoesNotContain("return-details",effective.ScreenIds);Assert.DoesNotContain("sales-list",effective.ScreenIds);

                Assert.True((await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=cashier.Id,ScreenIds=listed.Value.Where(item=>item.IsGranted).Select(item=>item.ScreenId).ToArray(),ActingUserId=adminId},CancellationToken.None)).Succeeded);
                Assert.Equal(new[]{"dashboard","returns-overview"},await store.GetScreenIdsAsync(cashier.Id,CancellationToken.None));
                var reloaded=await service.GetPermissionsAsync(cashier.Id,features,CancellationToken.None);
                Assert.True(reloaded.Value.Single(item=>item.ScreenId=="returns-overview").IsGranted);

                var administrator=await service.GetEffectiveAccessAsync(new AlKhayal.Application.Authentication.UserIdentity{Id=adminId,Role=UserRoles.Administrator},features,CancellationToken.None);
                Assert.Contains("users-list",administrator.ScreenIds);Assert.Contains("coming-soon",administrator.ScreenIds);Assert.Contains("return-details",administrator.ScreenIds);
                Assert.Equal(catalog.Screens.Count,administrator.ScreenIds.Count);
                Assert.Equal("invalid_screen",(await service.SetPermissionsAsync(new SetScreenPermissionsCommand{UserId=cashier.Id,ScreenIds=new[]{"legacy-sales"},ActingUserId=adminId},CancellationToken.None)).ErrorCode);
            }
            finally { SQLiteConnection.ClearAllPools(); if(File.Exists(path))File.Delete(path); }
        }

        [Fact]
        public void Json_screen_catalog_reads_permission_metadata_from_the_frontend_registry()
        {
            var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"frontend");
            try
            {
                var dataPath=Path.Combine(root,"data");Directory.CreateDirectory(dataPath);
                File.WriteAllText(Path.Combine(dataPath,"screens.json"),"{\"screens\":[{\"route\":\"alerts-list\",\"title\":\"التنبيهات\",\"module\":\"التنبيهات\",\"renderer\":\"alerts\",\"implemented\":true},{\"route\":\"reorder-alert\",\"title\":\"تنبيه حد الطلب\",\"module\":\"التنبيهات\",\"renderer\":\"legacy-redirect\",\"redirectTo\":\"alerts-list\",\"permissionControllable\":true},{\"route\":\"alert-alias\",\"title\":\"تنبيه قديم\",\"module\":\"التنبيهات\",\"renderer\":\"legacy-redirect\",\"redirectTo\":\"alerts-list\"},{\"route\":\"alert-details\",\"title\":\"تفاصيل التنبيه\",\"module\":\"التنبيهات\",\"renderer\":\"alerts\",\"permissionRoute\":\"alerts-list\"}]}",System.Text.Encoding.UTF8);
                var catalog=new JsonScreenCatalog(root);
                Assert.True(catalog.Find("alerts-list").Implemented);
                Assert.True(catalog.Find("reorder-alert").IsPermissionEntry);
                Assert.False(catalog.Find("alert-alias").IsPermissionEntry);
                Assert.False(catalog.Find("alert-details").IsPermissionEntry);
                Assert.Equal("alerts-list",catalog.Find("alert-details").PermissionRoute);
                Assert.Equal("تنبيه حد الطلب",catalog.Find("reorder-alert").ArabicName);
            }
            finally { var temporaryRoot=Path.GetDirectoryName(root);if(Directory.Exists(temporaryRoot))Directory.Delete(temporaryRoot,true); }
        }

        private sealed class Catalog:IScreenCatalog
        {
            public IReadOnlyList<ScreenDefinition> Screens{get;}=new[]{new ScreenDefinition{Id="dashboard",ArabicName="الرئيسية",Module="الرئيسية",Implemented=true},new ScreenDefinition{Id="pos",ArabicName="نقطة البيع",Module="المبيعات",FeatureKey="pos",Implemented=true},new ScreenDefinition{Id="sales-list",ArabicName="المبيعات",Module="المبيعات",FeatureKey="sales",Implemented=true},new ScreenDefinition{Id="users-list",ArabicName="المستخدمون",Module="الإعدادات",AdministratorOnly=true,Implemented=true}};
            public ScreenDefinition Find(string id){return Screens.FirstOrDefault(item=>string.Equals(item.Id,id,StringComparison.OrdinalIgnoreCase));}
        }

        private sealed class PermissionCatalog:IScreenCatalog
        {
            public IReadOnlyList<ScreenDefinition> Screens{get;}=new[]{
                new ScreenDefinition{Id="dashboard",ArabicName="الرئيسية",Module="الرئيسية",Implemented=true,Renderer="dashboard"},
                new ScreenDefinition{Id="returns-overview",ArabicName="نظرة عامة على المرتجعات",Module="المرتجعات",FeatureKey="returns",Implemented=true,Renderer="returns-management"},
                new ScreenDefinition{Id="return-details",ArabicName="تفاصيل المرتجع",Module="المرتجعات",Implemented=true,PermissionRoute="returns-overview",Renderer="return-details"},
                new ScreenDefinition{Id="sales-list",ArabicName="قائمة المبيعات",Module="المبيعات",Implemented=true,Renderer="sales-invoices"},
                new ScreenDefinition{Id="legacy-sales",ArabicName="مبيعات قديمة",Module="المبيعات",Implemented=true,RedirectTo="sales-list",Renderer="legacy-redirect"},
                new ScreenDefinition{Id="coming-soon",ArabicName="غير منفذة",Module="العملاء",Implemented=true,Renderer="under-construction"},
                new ScreenDefinition{Id="new-screen",ArabicName="شاشة جديدة",Module="العملاء",Implemented=true,Renderer="entity-list"},
                new ScreenDefinition{Id="users-list",ArabicName="المستخدمون والصلاحيات",Module="الإعدادات",AdministratorOnly=true,Implemented=true,Renderer="user-management"}
            };
            public ScreenDefinition Find(string id){return Screens.FirstOrDefault(item=>string.Equals(item.Id,id,StringComparison.OrdinalIgnoreCase));}
        }
    }
}
