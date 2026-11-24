using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using AlKhayal.Application.Backups;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Settings;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/settings")]
    public sealed class SettingsController : ApiController
    {
        private const string CookieName = "alkhayal_session";
        private readonly SettingsService _settings;
        private readonly AuthService _auth;
        private readonly IBusinessBackups _backups;
        public SettingsController(SettingsService settings, AuthService auth, IBusinessBackups backups = null)
        { _settings = settings; _auth = auth; _backups = backups; }

        [HttpGet, Route("company")]
        public async Task<IHttpActionResult> Company(CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            return Ok(ApiEnvelope<CompanyProfile>.Ok(await _settings.GetCompanyAsync(ct).ConfigureAwait(false)));
        }

        [HttpPut, Route("company")]
        public async Task<IHttpActionResult> Company(UpdateCompanyProfileRequest request, CancellationToken ct)
        {
            var check = await Administrator(ct, true).ConfigureAwait(false); if (check.Error != null) return check.Error;
            var result = await _settings.UpdateCompanyAsync(request == null ? null : request.ToCommand(check.User.Id), ct).ConfigureAwait(false);
            return Result(result);
        }

        [HttpGet, Route("general")]
        public async Task<IHttpActionResult> General(CancellationToken ct)
        {
            if (await CurrentUser(ct).ConfigureAwait(false) == null) return UnauthorizedResult();
            return Ok(ApiEnvelope<GeneralSettings>.Ok(await _settings.GetGeneralAsync(ct).ConfigureAwait(false)));
        }

        [HttpPut, Route("general")]
        public async Task<IHttpActionResult> General(UpdateGeneralSettingsRequest request, CancellationToken ct)
        {
            var check = await Administrator(ct, true).ConfigureAwait(false); if (check.Error != null) return check.Error;
            var result = await _settings.UpdateGeneralAsync(request == null ? null : request.ToCommand(check.User.Id), ct).ConfigureAwait(false);
            return Result(result);
        }

        [HttpGet, Route("database-info")]
        public async Task<IHttpActionResult> DatabaseInfo(CancellationToken ct)
        {
            var check = await Administrator(ct, false).ConfigureAwait(false); if (check.Error != null) return check.Error;
            return Ok(ApiEnvelope<DatabaseInformation>.Ok(await _settings.GetDatabaseInformationAsync(ct).ConfigureAwait(false)));
        }

        [HttpGet, Route("backups")]
        public Task<IHttpActionResult> Backups(CancellationToken ct) =>
            BackupAction(false, ct, () => _backups.StatusAsync());

        [HttpPut, Route("backups")]
        public Task<IHttpActionResult> ConfigureBackups(BackupConfiguration request, CancellationToken ct) =>
            BackupAction(true, ct, () => _backups.ConfigureAsync(request));

        [HttpPost, Route("backups/create")]
        public Task<IHttpActionResult> CreateBackup(ManualBackupDestination request, CancellationToken ct) =>
            BackupAction(true, ct, () => _backups.CreateAsync(request?.Directory));

        [HttpPost, Route("backups/inspect")]
        public Task<IHttpActionResult> InspectBackup(CancellationToken ct) =>
            BackupAction(true, ct, async () =>
            {
                if (Request.Content?.Headers.ContentType?.MediaType != "application/octet-stream")
                    throw new BackupException("اختر ملف نسخة احتياطية.");
                using (var stream = await Request.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    return await _backups.InspectAsync(stream, ct).ConfigureAwait(false);
            });

        [HttpPost, Route("backups/restore")]
        public async Task<IHttpActionResult> RestoreBackup(BackupConfirmation request, CancellationToken ct)
        {
            var check = await Administrator(ct, true).ConfigureAwait(false);
            if (check.Error != null) return check.Error;
            if (_backups == null) return BackupUnavailable();
            if (!Guid.TryParseExact(request?.Confirmation, "N", out _))
                return BackupFailure(new BackupException("يلزم فحص الملف وتأكيد الاستعادة."));
            if (!await BackupMaintenanceHandler.EnterAsync(Request, ct).ConfigureAwait(false))
                return BackupFailure(new BackupException("تعذر إيقاف العمليات الجارية للاستعادة. حاول مجددًا."));
            // A previously in-flight request could have revoked this administrator.
            return await BackupAction(true, ct, async () =>
            {
                var recovery = await _backups.RestoreAsync(request.Confirmation, ct).ConfigureAwait(false);
                _settings.InvalidateAfterRestore();
                return recovery;
            }).ConfigureAwait(false);
        }

        [HttpGet, Route("backups/download")]
        public async Task<IHttpActionResult> DownloadBackup(string name, CancellationToken ct)
        {
            var check = await Administrator(ct, false).ConfigureAwait(false);
            if (check.Error != null) return check.Error;
            if (_backups == null) return BackupUnavailable();
            try
            {
                var stream = await _backups.DownloadAsync(name).ConfigureAwait(false);
                var response = Request.CreateResponse(HttpStatusCode.OK);
                response.Content = new StreamContent(stream);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = name };
                response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
                return ResponseMessage(response);
            }
            catch (Exception ex) when (IsBackupFailure(ex)) { return BackupFailure(ex); }
        }

        private async Task<IHttpActionResult> BackupAction<T>(bool csrf, CancellationToken ct, Func<Task<T>> action)
        {
            var check = await Administrator(ct, csrf).ConfigureAwait(false);
            if (check.Error != null) return check.Error;
            if (_backups == null) return BackupUnavailable();
            try { return Ok(ApiEnvelope<T>.Ok(await action().ConfigureAwait(false))); }
            catch (Exception ex) when (IsBackupFailure(ex)) { return BackupFailure(ex); }
        }

        private IHttpActionResult BackupUnavailable() => Content(HttpStatusCode.ServiceUnavailable,
            ApiEnvelope<object>.Fail("backup_unavailable", "خدمة النسخ الاحتياطي غير متاحة."));

        private static bool IsBackupFailure(Exception ex) => ex is BackupException || ex is IOException || ex is InvalidDataException ||
            ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException ||
            ex is System.Data.Common.DbException || ex is FormatException || ex is NotSupportedException;

        private IHttpActionResult BackupFailure(Exception ex)
        {
            Trace.TraceWarning("Backup request failed at {0}: {1}", Request.RequestUri.AbsolutePath, ex.GetType().Name);
            return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("backup_failed",
                ex is BackupException ? ex.Message : "تعذرت العملية. تحقق من سلامة الملف ومساحة القرص وصلاحيات الوصول."));
        }
        private IHttpActionResult Result<T>(SettingsResult<T> result) { return result.Succeeded ? Ok(ApiEnvelope<T>.Ok(result.Value)) : Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail(result.ErrorCode, result.ErrorMessage)); }
        private async Task<AuthCheck> Administrator(CancellationToken ct, bool csrf)
        {
            var user = await CurrentUser(ct).ConfigureAwait(false);
            if (user == null) return new AuthCheck { Error = UnauthorizedResult() };
            if (user.Role != UserRoles.Administrator) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("forbidden", "تعديل الإعدادات متاح لمسؤول النظام فقط.")) };
            if (csrf && !await _auth.ValidateCsrfAsync(ReadToken(), Header("X-CSRF-Token"), ct).ConfigureAwait(false)) return new AuthCheck { Error = Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("invalid_csrf", "تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى.")) };
            return new AuthCheck { User = user };
        }
        private Task<UserIdentity> CurrentUser(CancellationToken ct) { return _auth.GetCurrentUserAsync(ReadToken(), ct); }
        private IHttpActionResult UnauthorizedResult() { return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولاً.")); }
        private string ReadToken() { IEnumerable<string> values; if (!Request.Headers.TryGetValues("Cookie", out values)) return null; foreach (var header in values) foreach (var part in header.Split(';')) { var pair = part.Trim().Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == CookieName) return pair[1]; } return null; }
        private string Header(string name) { IEnumerable<string> values; return Request.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null; }
        private sealed class AuthCheck { public UserIdentity User { get; set; } public IHttpActionResult Error { get; set; } }
    }

    public sealed class ManualBackupDestination
    {
        public string Directory { get; set; }
    }

    public sealed class BackupConfirmation
    {
        public string Confirmation { get; set; }
    }

    public sealed class UpdateCompanyProfileRequest
    {
        public string BusinessName { get; set; } public string DisplayName { get; set; } public string Phone { get; set; } public string Email { get; set; } public string Address { get; set; } public string TaxRegistrationNumber { get; set; } public string CommercialRegistrationNumber { get; set; } public string FooterText { get; set; }
        public UpdateCompanyProfileCommand ToCommand(long userId) { return new UpdateCompanyProfileCommand { BusinessName=BusinessName, DisplayName=DisplayName, Phone=Phone, Email=Email, Address=Address, TaxRegistrationNumber=TaxRegistrationNumber, CommercialRegistrationNumber=CommercialRegistrationNumber, FooterText=FooterText, ChangedByUserId=userId }; }
    }

    public sealed class UpdateGeneralSettingsRequest
    {
        public string LanguageCode { get; set; } public string DateFormat { get; set; } public string TimeFormat { get; set; } public bool UseGroupSeparators { get; set; } public string CurrencyCode { get; set; } public string CurrencySymbol { get; set; } public int DecimalPrecision { get; set; } public bool TaxEnabled { get; set; } public decimal DefaultTaxRate { get; set; } public bool PricesIncludeTax { get; set; }
        public bool? ShowCashChangeCalculator { get; set; }
        public UpdateGeneralSettingsCommand ToCommand(long userId) { return new UpdateGeneralSettingsCommand { LanguageCode=LanguageCode, DateFormat=DateFormat, TimeFormat=TimeFormat, UseGroupSeparators=UseGroupSeparators, CurrencyCode=CurrencyCode, CurrencySymbol=CurrencySymbol, DecimalPrecision=DecimalPrecision, TaxEnabled=TaxEnabled, DefaultTaxRate=DefaultTaxRate, PricesIncludeTax=PricesIncludeTax, ShowCashChangeCalculatorOverride=ShowCashChangeCalculator, ChangedByUserId=userId }; }
    }
}
