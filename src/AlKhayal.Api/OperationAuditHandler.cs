using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Operations;

namespace AlKhayal.Api
{
    public sealed class OperationAuditHandler : DelegatingHandler
    {
        private const string CookieName = "alkhayal_session";
        private readonly AuthService _auth;
        private readonly OperationLogService _logs;

        public OperationAuditHandler(AuthService auth, OperationLogService logs)
        {
            _auth = auth ?? throw new ArgumentNullException("auth");
            _logs = logs ?? throw new ArgumentNullException("logs");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri == null ? string.Empty : request.RequestUri.AbsolutePath;
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/operation-logs", StringComparison.OrdinalIgnoreCase)
                || !IsAuditableMethod(request.Method.Method))
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var token = ReadSession(request);
            UserIdentity actor = await TryGetUserAsync(token, cancellationToken).ConfigureAwait(false);
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await RecordAsync(actor, request, path, 500, cancellationToken).ConfigureAwait(false);
                Trace.TraceError("API mutation threw before returning a response: {0} {1}; {2}", request.Method.Method, path, ex.GetType().Name);
                throw;
            }

            if (actor == null && response != null)
                actor = await TryGetUserAsync(ReadSession(response), cancellationToken).ConfigureAwait(false);
            if (actor != null && response != null)
                await RecordAsync(actor, request, path, (int)response.StatusCode, cancellationToken).ConfigureAwait(false);
            return response;
        }

        private async Task<UserIdentity> TryGetUserAsync(string token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            try { return await _auth.GetCurrentUserAsync(token, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Trace.TraceError("Operation audit could not resolve the request actor: {0}", ex.GetType().Name);
                return null;
            }
        }

        private async Task RecordAsync(UserIdentity actor, HttpRequestMessage request, string path, int statusCode, CancellationToken cancellationToken)
        {
            if (actor == null) return;
            try
            {
                await _logs.RecordAsync(actor.Id, actor.Username, actor.DisplayName, request.Method.Method, path, statusCode, statusCode >= 200 && statusCode < 400, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Do not turn an already completed money operation into an ambiguous client retry.
                // Its source-domain audit remains atomic with the original database transaction.
                Trace.TraceError("Activity audit append failed after API response: {0} {1}, status {2}; {3}", request.Method.Method, path, statusCode, ex.GetType().Name);
            }
        }

        private static string ReadSession(HttpRequestMessage request)
        {
            if (request.Headers.TryGetValues("Cookie", out var values))
                return ReadCookie(values.SelectMany(value => value.Split(';')));
            return null;
        }

        private static bool IsAuditableMethod(string method)
        {
            return string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadSession(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                return ReadCookie(values);
            return null;
        }

        private static string ReadCookie(System.Collections.Generic.IEnumerable<string> cookies)
        {
            var prefix = CookieName + "=";
            foreach (var value in cookies)
            {
                var item = value.Trim();
                if (item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return item.Substring(prefix.Length).Split(';')[0].Trim();
            }
            return null;
        }
    }
}
