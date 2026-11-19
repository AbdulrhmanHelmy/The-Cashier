using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Features;

namespace AlKhayal.Application.Users
{
    public sealed class UserManagementService
    {
        private readonly IUserManagementStore _store;
        private readonly IScreenCatalog _screens;
        private readonly IPasswordHasher _hasher;
        public UserManagementService(IUserManagementStore store, IScreenCatalog screens, IPasswordHasher hasher) { _store = store; _screens = screens; _hasher = hasher; }

        public Task<IReadOnlyList<ManagedUser>> ListAsync(CancellationToken ct) { return _store.ListAsync(ct); }
        public Task<ManagedUser> GetAsync(long userId, CancellationToken ct) { return _store.FindAsync(userId, ct); }
        public Task<bool> CanEditSalePricesAsync(UserIdentity user, CancellationToken ct)
        {
            if (user == null) return Task.FromResult(false);
            return user.Role == UserRoles.Administrator ? Task.FromResult(true) : _store.CanEditSalePricesAsync(user.Id, ct);
        }

        public Task<UserManagementResult<bool>> SetSalePricePermissionAsync(long userId, bool canEdit, long actingUserId, CancellationToken ct)
        {
            if (userId <= 0 || actingUserId <= 0) return Task.FromResult(UserManagementResult<bool>.Fail("validation", "المستخدم المطلوب غير صالح."));
            return _store.SetSalePricePermissionAsync(userId, canEdit, actingUserId, ct);
        }
        public Task<bool> CanViewPosStockQuantitiesAsync(UserIdentity user, CancellationToken ct)
        {
            if (user == null) return Task.FromResult(false);
            return user.Role == UserRoles.Administrator ? Task.FromResult(true) : _store.CanViewPosStockQuantitiesAsync(user.Id, ct);
        }
        public Task<UserManagementResult<bool>> SetPosStockPermissionAsync(long userId, bool canView, long actingUserId, CancellationToken ct)
        {
            if (userId <= 0 || actingUserId <= 0) return Task.FromResult(UserManagementResult<bool>.Fail("validation", "المستخدم المطلوب غير صالح."));
            return _store.SetPosStockPermissionAsync(userId, canView, actingUserId, ct);
        }

        public async Task<UserManagementResult<ManagedUser>> CreateAsync(CreateUserCommand command, CancellationToken ct)
        {
            var validation = Validate(command == null ? null : command.Username, command == null ? null : command.DisplayName, command == null ? null : command.Role);
            if (validation != null) return UserManagementResult<ManagedUser>.Fail("validation", validation);
            if (string.IsNullOrEmpty(command.Password)) return UserManagementResult<ManagedUser>.Fail("validation", "كلمة المرور مطلوبة.");
            command.Username = command.Username.Trim(); command.DisplayName = command.DisplayName.Trim(); command.Role = command.Role.ToLowerInvariant();
            try { return UserManagementResult<ManagedUser>.Ok(await _store.CreateAsync(command, _hasher.Hash(command.Password), ct).ConfigureAwait(false)); }
            catch (InvalidOperationException exception) { return UserManagementResult<ManagedUser>.Fail("duplicate_username", exception.Message); }
        }

        public async Task<UserManagementResult<ManagedUser>> UpdateAsync(UpdateUserCommand command, CancellationToken ct)
        {
            var validation = Validate(command == null ? null : command.Username, command == null ? null : command.DisplayName, command == null ? null : command.Role);
            if (validation != null) return UserManagementResult<ManagedUser>.Fail("validation", validation);
            command.Username = command.Username.Trim(); command.DisplayName = command.DisplayName.Trim(); command.Role = command.Role.ToLowerInvariant();
            try { return await _store.UpdateAsync(command, ct).ConfigureAwait(false); }
            catch (InvalidOperationException exception) { return UserManagementResult<ManagedUser>.Fail("duplicate_username", exception.Message); }
        }

        public Task<UserManagementResult<ManagedUser>> SetStatusAsync(SetUserStatusCommand command, CancellationToken ct)
        {
            if (command == null || command.UserId <= 0) return Task.FromResult(UserManagementResult<ManagedUser>.Fail("validation", "المستخدم المطلوب غير صالح."));
            return _store.SetStatusAsync(command, ct);
        }

        public Task<UserManagementResult<bool>> ResetPasswordAsync(ResetUserPasswordCommand command, CancellationToken ct)
        {
            if (command == null || command.UserId <= 0 || string.IsNullOrEmpty(command.Password)) return Task.FromResult(UserManagementResult<bool>.Fail("validation", "المستخدم وكلمة المرور الجديدة مطلوبان."));
            return _store.ResetPasswordAsync(command, _hasher.Hash(command.Password), ct);
        }

        public async Task<UserManagementResult<IReadOnlyList<CashierScreenPermission>>> GetPermissionsAsync(long userId, FeatureSnapshot features, CancellationToken ct)
        {
            var user = await _store.FindAsync(userId, ct).ConfigureAwait(false);
            if (user == null) return UserManagementResult<IReadOnlyList<CashierScreenPermission>>.Fail("not_found", "المستخدم غير موجود.");
            if (user.Role != UserRoles.Cashier) return UserManagementResult<IReadOnlyList<CashierScreenPermission>>.Fail("invalid_role", "صلاحيات الشاشات متاحة لحسابات الكاشير فقط.");
            var granted = EffectiveGrantIds(await _store.GetScreenIdsAsync(userId, ct).ConfigureAwait(false));
            var enabled = EnabledFeatures(features);
            var result = _screens.Screens.Where(item => item.IsPermissionEntry).Select(item =>
            {
                var moduleEnabled = string.IsNullOrWhiteSpace(item.FeatureKey) || enabled.Contains(item.FeatureKey);
                return new CashierScreenPermission { ScreenId = item.Id, ArabicName = item.ArabicName, Module = item.Module, FeatureKey = item.FeatureKey, IsGranted = granted.Contains(item.Id), IsModuleEnabled = moduleEnabled, IsEffective = granted.Contains(item.Id) && moduleEnabled };
            }).ToList();
            return UserManagementResult<IReadOnlyList<CashierScreenPermission>>.Ok(result);
        }

        public async Task<UserManagementResult<bool>> SetPermissionsAsync(SetScreenPermissionsCommand command, CancellationToken ct)
        {
            if (command == null || command.UserId <= 0 || command.ScreenIds == null) return UserManagementResult<bool>.Fail("validation", "قائمة صلاحيات الشاشات مطلوبة.");
            var normalized = command.ScreenIds.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var id in normalized)
            {
                var screen = _screens.Find(id);
                if (screen == null) return UserManagementResult<bool>.Fail("invalid_screen", "معرف الشاشة غير معروف: " + id);
                if (screen.AdministratorOnly) return UserManagementResult<bool>.Fail("administrator_only_screen", "لا يمكن منح شاشة إدارية لحساب كاشير.");
                if (!screen.IsPermissionEntry && string.IsNullOrWhiteSpace(screen.PermissionRoute)) return UserManagementResult<bool>.Fail("invalid_screen", "معرف الشاشة غير قابل للتحكم بصلاحية مستقلة: " + id);
                if (!string.IsNullOrWhiteSpace(screen.PermissionRoute))
                {
                    var canonical = _screens.Find(screen.PermissionRoute);
                    if (canonical == null || !canonical.IsPermissionEntry) return UserManagementResult<bool>.Fail("invalid_screen", "مسار الصلاحية البديل غير صالح: " + id);
                }
            }
            command.ScreenIds = EffectiveGrantIds(normalized).ToList();
            return await _store.SetScreenIdsAsync(command, ct).ConfigureAwait(false);
        }

        public async Task<EffectiveScreenAccess> GetEffectiveAccessAsync(UserIdentity user, FeatureSnapshot features, CancellationToken ct)
        {
            var enabled = EnabledFeatures(features);
            IEnumerable<ScreenDefinition> candidates;
            if (user.Role == UserRoles.Administrator) candidates = _screens.Screens;
            else
            {
                var granted = EffectiveGrantIds(await _store.GetScreenIdsAsync(user.Id, ct).ConfigureAwait(false));
                candidates = _screens.Screens.Where(item => item.IsPermissionEntry && granted.Contains(item.Id));
            }
            return new EffectiveScreenAccess { Role = user.Role, ScreenIds = candidates.Where(item => string.IsNullOrWhiteSpace(item.FeatureKey) || enabled.Contains(item.FeatureKey)).Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), CanEditSalePrices = await CanEditSalePricesAsync(user, ct).ConfigureAwait(false), CanViewPosStockQuantities = await CanViewPosStockQuantitiesAsync(user, ct).ConfigureAwait(false), GeneratedUtc = DateTime.UtcNow };
        }

        private static HashSet<string> EnabledFeatures(FeatureSnapshot snapshot) { return new HashSet<string>((snapshot == null ? new EffectiveFeature[0] : snapshot.Modules.Concat(snapshot.Capabilities)).Where(item => item.IsEnabled).Select(item => item.Key), StringComparer.OrdinalIgnoreCase); }
        private HashSet<string> EffectiveGrantIds(IEnumerable<string> storedIds)
        {
            var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var storedId in storedIds ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(storedId)) continue;
                var screen = _screens.Find(storedId.Trim());
                if (screen == null || screen.AdministratorOnly) continue;
                if (screen.IsPermissionEntry) granted.Add(screen.Id);
                else if (!string.IsNullOrWhiteSpace(screen.PermissionRoute))
                {
                    var canonical = _screens.Find(screen.PermissionRoute);
                    if (canonical != null && canonical.IsPermissionEntry) granted.Add(canonical.Id);
                }
            }
            return granted;
        }
        private static string Validate(string username, string displayName, string role)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > 64) return "اسم المستخدم مطلوب وبحد أقصى 64 حرفًا.";
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 120) return "الاسم المعروض مطلوب وبحد أقصى 120 حرفًا.";
            var normalizedRole = string.IsNullOrWhiteSpace(role) ? null : role.Trim().ToLowerInvariant();
            if (!UserRoles.IsValid(normalizedRole)) return "نوع الحساب يجب أن يكون مسؤول نظام أو كاشير.";
            return null;
        }
    }
}
