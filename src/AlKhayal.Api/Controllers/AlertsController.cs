using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Alerts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Features;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/alerts")]
    public sealed class AlertsController : ApiController
    {
        private readonly AlertService _alerts;
        private readonly AuthService _auth;
        private readonly UserManagementService _users;
        private readonly FeatureService _features;
        public AlertsController(AlertService alerts,AuthService auth,UserManagementService users,FeatureService features)
        { _alerts=alerts; _auth=auth; _users=users; _features=features; }
        [HttpGet,Route("")]
        public async Task<IHttpActionResult> Get(CancellationToken ct)
        {
            var user=await _auth.GetCurrentUserAsync(ReadToken(),ct).ConfigureAwait(false);
            if(user==null)return Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولًا."));
            var access=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);
            var full=access.ScreenIds.Any(x=>x=="alerts-list");
            if(!full && !access.ScreenIds.Any(x=>x=="reorder-alert" || x=="low-stock-alert"))
                return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية التنبيهات."));
            return Ok(ApiEnvelope<IReadOnlyList<AlertRecord>>.Ok(await _alerts.ActiveAsync(user.Id,!full,ct).ConfigureAwait(false)));
        }
        [HttpPost,Route("{id}/read")]
        public async Task<IHttpActionResult> SetRead(string id, AlertReadRequest request, CancellationToken ct)
        {
            var user=await _auth.GetCurrentUserAsync(ReadToken(),ct).ConfigureAwait(false);
            if(user==null)return Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولًا."));
            if(!await HasValidCsrf(ct))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب."));
            var access=await AlertAccess(user,ct).ConfigureAwait(false);
            if(!access.Item1)return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية التنبيهات."));
            if(string.IsNullOrWhiteSpace(id) || request==null)return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation","بيانات التنبيه غير صالحة."));
            if(!access.Item2 && !id.StartsWith("stock:",System.StringComparison.Ordinal))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية هذا النوع من التنبيهات."));
            await _alerts.SetReadAsync(user.Id,id,request.Read,ct).ConfigureAwait(false);
            return Ok(ApiEnvelope<object>.Ok(null));
        }
        [HttpPost,Route("read-all")]
        public async Task<IHttpActionResult> ReadAll(CancellationToken ct)
        {
            var user=await _auth.GetCurrentUserAsync(ReadToken(),ct).ConfigureAwait(false);
            if(user==null)return Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولًا."));
            if(!await HasValidCsrf(ct))return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب."));
            var access=await AlertAccess(user,ct).ConfigureAwait(false);
            if(!access.Item1)return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية التنبيهات."));
            await _alerts.MarkAllReadAsync(user.Id,!access.Item2,ct).ConfigureAwait(false);
            return Ok(ApiEnvelope<object>.Ok(null));
        }
        private async Task<System.Tuple<bool,bool>> AlertAccess(UserIdentity user,CancellationToken ct)
        {
            var access=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);
            var full=access.ScreenIds.Any(x=>x=="alerts-list");
            return System.Tuple.Create(full || access.ScreenIds.Any(x=>x=="reorder-alert" || x=="low-stock-alert"),full);
        }
        private async Task<bool> HasValidCsrf(CancellationToken ct)
        { return await _auth.ValidateCsrfAsync(ReadToken(),Request.Headers.Contains("X-CSRF-Token")?Request.Headers.GetValues("X-CSRF-Token").FirstOrDefault():null,ct).ConfigureAwait(false); }
        private string ReadToken()
        {
            IEnumerable<string> values;if(!Request.Headers.TryGetValues("Cookie",out values))return null;
            foreach(var header in values)foreach(var part in header.Split(';'))
            { var pair=part.Trim().Split(new[]{'='},2);if(pair.Length==2 && pair[0]=="alkhayal_session")return pair[1]; }
            return null;
        }
        public sealed class AlertReadRequest { public bool Read { get; set; } }
    }
}
