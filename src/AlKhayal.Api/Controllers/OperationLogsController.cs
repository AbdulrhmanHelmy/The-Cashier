using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Operations;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/operation-logs")]
    public sealed class OperationLogsController : ApiController
    {
        private const string CookieName = "alkhayal_session";
        private readonly AuthService _auth;
        private readonly OperationLogService _logs;

        public OperationLogsController(AuthService auth, OperationLogService logs)
        {
            _auth = auth;
            _logs = logs;
        }

        [HttpGet, Route("")]
        public async Task<IHttpActionResult> List(int page = 1, int pageSize = 50, string search = null, string from = null, string to = null, long? userId = null, string outcome = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var user = await _auth.GetCurrentUserAsync(ReadSession(), cancellationToken).ConfigureAwait(false);
            if (user == null) return Content(HttpStatusCode.Unauthorized, ApiEnvelope<object>.Fail("unauthorized", "يرجى تسجيل الدخول أولًا."));
            if (user.Role != UserRoles.Administrator) return Content(HttpStatusCode.Forbidden, ApiEnvelope<object>.Fail("administrator_required", "سجل العمليات متاح لمسؤول النظام فقط."));
            try
            {
                var result = await _logs.ListAsync(new OperationLogQuery { Page = page, PageSize = pageSize, Search = search, From = from, To = to, ActorUserId = userId, Outcome = outcome }, cancellationToken).ConfigureAwait(false);
                return Ok(ApiEnvelope<OperationLogPage>.Ok(result));
            }
            catch (ArgumentException ex) { return Content(HttpStatusCode.BadRequest, ApiEnvelope<object>.Fail("validation", ex.Message)); }
        }

        private string ReadSession()
        {
            if (!Request.Headers.TryGetValues("Cookie", out var values)) return null;
            var prefix = CookieName + "=";
            foreach (var value in values)
                foreach (var cookie in value.Split(';'))
                {
                    var item = cookie.Trim();
                    if (item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return item.Substring(prefix.Length);
                }
            return null;
        }
    }
}
