using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Exports;
using AlKhayal.Application.Features;
using AlKhayal.Application.Users;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/exports")]
    public sealed class ExportsController : ApiController
    {
        private readonly ExportService exports; private readonly AuthService auth; private readonly UserManagementService users; private readonly FeatureService features;
        public ExportsController(ExportService exports,AuthService auth,UserManagementService users,FeatureService features){this.exports=exports;this.auth=auth;this.users=users;this.features=features;}

        [HttpGet,Route("{dataset}/{format}")]
        public async Task<HttpResponseMessage> Download(string dataset,string format,CancellationToken ct)
        {
            var definition=exports.Find(dataset);if(definition==null)return Error(HttpStatusCode.BadRequest,"invalid_dataset","مجموعة بيانات التصدير غير مدعومة.");
            var user=await auth.GetCurrentUserAsync(ReadCookie(),ct).ConfigureAwait(false);if(user==null)return Error(HttpStatusCode.Unauthorized,"unauthorized","يرجى تسجيل الدخول أولًا.");
            if(user.Role!=UserRoles.Administrator){var access=await users.GetEffectiveAccessAsync(user,await features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);if(!access.ScreenIds.Contains(definition.PermissionRoute,StringComparer.OrdinalIgnoreCase))return Error(HttpStatusCode.Forbidden,"screen_forbidden","لا تملك صلاحية قراءة هذه البيانات.");}
            try
            {
                var filters=Request.GetQueryNameValuePairs().Where(x=>!string.IsNullOrWhiteSpace(x.Key)).ToDictionary(x=>x.Key,x=>x.Value??"",StringComparer.OrdinalIgnoreCase);
                var result=await exports.ExportAsync(dataset,format,filters,ct).ConfigureAwait(false);
                var response=Request.CreateResponse(HttpStatusCode.OK);response.Content=new ByteArrayContent(result.Content);response.Content.Headers.ContentType=MediaTypeHeaderValue.Parse(result.ContentType);response.Content.Headers.ContentDisposition=new ContentDispositionHeaderValue("attachment"){FileName=result.FileName};response.Headers.CacheControl=new CacheControlHeaderValue{NoCache=true,NoStore=true};return response;
            }
            catch(ArgumentException ex){return Error(HttpStatusCode.BadRequest,"invalid_export_request",ex.Message);}
        }
        private HttpResponseMessage Error(HttpStatusCode status,string code,string message)=>Request.CreateResponse(status,ApiEnvelope<object>.Fail(code,message));
        private string ReadCookie(){IEnumerable<string> values;if(!Request.Headers.TryGetValues("Cookie",out values))return null;foreach(var header in values)foreach(var part in header.Split(';')){var pair=part.Trim().Split(new[]{'='},2);if(pair.Length==2&&pair[0]=="alkhayal_session")return pair[1];}return null;}
    }
}
