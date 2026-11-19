using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using AlKhayal.Api.Contracts;
using AlKhayal.Application.Authentication;
using AlKhayal.Application.Features;
using AlKhayal.Application.Sales;
using AlKhayal.Application.Users;
using AlKhayal.Core.Models;

namespace AlKhayal.Api.Controllers
{
    [RoutePrefix("api/sales")]
    public sealed class SalesController : ApiController
    {
        private readonly SalesService _sales;private readonly AuthService _auth;private readonly UserManagementService _users;private readonly FeatureService _features;
        public SalesController(SalesService sales,AuthService auth,UserManagementService users,FeatureService features){_sales=sales;_auth=auth;_users=users;_features=features;}
        [HttpGet,Route("")] public async Task<IHttpActionResult> List(string search=null,string from=null,string to=null,CancellationToken ct=default(CancellationToken)){var a=await Require(ct,false).ConfigureAwait(false);if(a.Error!=null)return a.Error;try{return Ok(ApiEnvelope<object>.Ok(await _sales.ListAsync(search,from,to,ct).ConfigureAwait(false)));}catch(ArgumentException ex){return Bad(ex.Message);}}
        [HttpGet,Route("suspended-count")] public async Task<IHttpActionResult> SuspendedCount(CancellationToken ct){var a=await Require(ct,false).ConfigureAwait(false);if(a.Error!=null)return a.Error;return Ok(ApiEnvelope<object>.Ok(new{count=await _sales.CountSuspendedAsync(ct).ConfigureAwait(false)}));}
        [HttpGet,Route("{id:long}")] public async Task<IHttpActionResult> Get(long id,CancellationToken ct){var a=await Require(ct,false).ConfigureAwait(false);if(a.Error!=null)return a.Error;var sale=await _sales.GetForEditAsync(id,a.User.Id,ct).ConfigureAwait(false);return sale==null?Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found","الفاتورة غير موجودة.")):Ok(ApiEnvelope<SaleInvoice>.Ok(sale));}
        [HttpGet,Route("installments/{id:long}/invoice")]
        public async Task<IHttpActionResult> InstallmentInvoice(long id,CancellationToken ct)
        {
            var access=await Require(ct,false,"installment-payment").ConfigureAwait(false);if(access.Error!=null)return access.Error;
            var sale=await _sales.GetAsync(id,ct).ConfigureAwait(false);
            return sale==null||!sale.InstallmentContractId.HasValue?Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found","فاتورة التقسيط غير موجودة.")):Ok(ApiEnvelope<SaleInvoice>.Ok(sale));
        }
        [HttpPost,Route("drafts")] public async Task<IHttpActionResult> Create(SaleRequest request,CancellationToken ct){var a=await Require(ct,true).ConfigureAwait(false);if(a.Error!=null)return a.Error;if(!await CanUseManualPrices(a.User,request==null?null:request.Lines,ct).ConfigureAwait(false))return PriceForbidden();try{return Content(HttpStatusCode.Created,ApiEnvelope<SaleInvoice>.Ok(await _sales.CreateDraftAsync(Map(request),a.User.Id,ct).ConfigureAwait(false)));}catch(ArgumentException ex){return Bad(ex.Message);}catch(System.Data.SQLite.SQLiteException){return Conflict("تعذر تعليق الفاتورة.");}}
        [HttpPost,Route("installments")]
        public async Task<IHttpActionResult> CreateInstallment(InstallmentSalePostRequest request,CancellationToken ct)
        {
            var access=await Require(ct,true,"installment-system").ConfigureAwait(false);if(access.Error!=null)return access.Error;
            if(!await CanUseManualPrices(access.User,request==null||request.Invoice==null?null:request.Invoice.Lines,ct).ConfigureAwait(false))return PriceForbidden();
            try
            {
                var command=request==null?null:new InstallmentSaleCommand{Invoice=Map(request.Invoice),DownPayment=request.DownPayment,InterestRate=request.InterestRate,InterestReturnPolicy=request.InterestReturnPolicy,InstallmentCount=request.InstallmentCount,IntervalMonths=request.IntervalMonths,FirstDueOn=request.FirstDueOn,TreasuryAccountId=request.TreasuryAccountId,OperationKey=request.OperationKey};
                var result=await _sales.CreateInstallmentAsync(command,access.User.Id,ct).ConfigureAwait(false);
                if(!result.Succeeded)return Content(result.ErrorCode=="validation"?HttpStatusCode.BadRequest:HttpStatusCode.Conflict,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));
                return Content(result.Replayed?HttpStatusCode.OK:HttpStatusCode.Created,ApiEnvelope<InstallmentSaleResult>.Ok(result));
            }
            catch(ArgumentException ex){return Bad(ex.Message);}
        }
        [HttpPut,Route("{id:long}")] public async Task<IHttpActionResult> Update(long id,SaleRequest request,CancellationToken ct){var a=await Require(ct,true).ConfigureAwait(false);if(a.Error!=null)return a.Error;if(!await CanUseManualPrices(a.User,request==null?null:request.Lines,ct).ConfigureAwait(false))return PriceForbidden();try{var sale=await _sales.UpdateDraftAsync(id,Map(request),a.User.Id,ct).ConfigureAwait(false);return sale==null?Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found","الفاتورة غير موجودة.")):Ok(ApiEnvelope<SaleInvoice>.Ok(sale));}catch(ArgumentException ex){return Bad(ex.Message);}catch(InvalidOperationException ex){return Conflict(ex.Message);}catch(System.Data.SQLite.SQLiteException){return Conflict("تعذر تعليق الفاتورة.");}}
        [HttpPost,Route("{id:long}/post")] public async Task<IHttpActionResult> Post(long id,SalePostRequest request,CancellationToken ct){var a=await Require(ct,true).ConfigureAwait(false);if(a.Error!=null)return a.Error;var result=await _sales.PostAsync(id,request==null?null:request.OperationKey,a.User.Id,ct).ConfigureAwait(false);if(result==null)return Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found","الفاتورة غير موجودة."));if(!result.Succeeded)return Content(result.ErrorCode=="not_found"?HttpStatusCode.NotFound:result.ErrorCode=="invalid_operation"?HttpStatusCode.BadRequest:HttpStatusCode.Conflict,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));return Ok(ApiEnvelope<SaleInvoice>.Ok(result.Invoice));}
        [HttpPost,Route("{id:long}/payments")] public async Task<IHttpActionResult> Payment(long id,SalePaymentRequest request,CancellationToken ct){var a=await Require(ct,true).ConfigureAwait(false);if(a.Error!=null)return a.Error;try{var result=await _sales.AddPaymentAsync(id,request==null?null:new SalePaymentCommand{Amount=request.Amount,Method=request.Method,TreasuryAccountId=request.TreasuryAccountId,OperationKey=request.OperationKey,OccurredOn=request.OccurredOn,Notes=request.Notes},a.User.Id,ct).ConfigureAwait(false);if(result==null)return Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found","الفاتورة غير موجودة."));if(!result.Succeeded)return Content(result.ErrorCode=="validation"?HttpStatusCode.BadRequest:HttpStatusCode.Conflict,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));return Ok(ApiEnvelope<SalePaymentResult>.Ok(result));}catch(ArgumentException ex){return Bad(ex.Message);}catch(AlKhayal.Application.Treasury.TreasuryException ex){return Content(HttpStatusCode.Conflict,ApiEnvelope<object>.Fail("treasury_"+ex.Code,ex.Message));}}
        [HttpPost,Route("{id:long}/returns")] public async Task<IHttpActionResult> Return(long id,SaleReturnRequest request,CancellationToken ct){var a=await Require(ct,true,"sales-returns").ConfigureAwait(false);if(a.Error!=null)return a.Error;try{var command=request==null?null:new SaleReturnCommand{OperationKey=request.OperationKey,Reason=request.Reason,InterestSettlement=request.InterestSettlement,Lines=(request.Lines??new List<SaleReturnLineRequest>()).Select(x=>x==null?null:new SaleReturnLineCommand{InvoiceLineId=x.InvoiceLineId,Quantity=x.Quantity,IsDamaged=x.IsDamaged,Serials=x.Serials??new List<string>()}).ToList()};var result=await _sales.ReturnAsync(id,command,a.User.Id,ct).ConfigureAwait(false);if(result==null)return Content(HttpStatusCode.NotFound,ApiEnvelope<object>.Fail("not_found","الفاتورة غير موجودة."));if(!result.Succeeded)return Content(result.ErrorCode=="validation"?HttpStatusCode.BadRequest:HttpStatusCode.Conflict,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));return Ok(ApiEnvelope<SaleReturnResult>.Ok(result));}catch(ArgumentException ex){return Bad(ex.Message);}}
        [HttpPost,Route("{id:long}/delete-draft")] public async Task<IHttpActionResult> DeleteDraft(long id,SalePostRequest request,CancellationToken ct){var a=await Require(ct,true,"sales-list").ConfigureAwait(false);if(a.Error!=null)return a.Error;var result=await _sales.DeleteDraftAsync(id,request==null?null:request.OperationKey,a.User.Id,ct).ConfigureAwait(false);return CancellationResponse(result);}
        [HttpPost,Route("{id:long}/cancel")] public async Task<IHttpActionResult> Cancel(long id,SaleCancelRequest request,CancellationToken ct){var a=await Require(ct,true,"sales-list").ConfigureAwait(false);if(a.Error!=null)return a.Error;var result=await _sales.CancelAsync(id,request==null?null:request.OperationKey,request==null?null:request.Reason,a.User.Id,ct).ConfigureAwait(false);return CancellationResponse(result);}
        [HttpPost,Route("{id:long}/corrections")] public async Task<IHttpActionResult> Correct(long id,SaleCorrectionRequest request,CancellationToken ct)
        {
            var a=await Require(ct,true).ConfigureAwait(false);if(a.Error!=null)return a.Error;
            if(!await CanUseManualPrices(a.User,request==null||request.Invoice==null?null:request.Invoice.Lines,ct).ConfigureAwait(false))return PriceForbidden();
            var command=request==null?null:new SaleCorrectionCommand{ExpectedVersion=request.ExpectedVersion,OperationKey=request.OperationKey,SettlementKind=request.SettlementKind,SettlementMethod=request.SettlementMethod,TreasuryAccountId=request.TreasuryAccountId,Invoice=Map(request.Invoice)};
            var result=await _sales.CorrectAsync(id,command,a.User.Id,ct).ConfigureAwait(false);
            if(!result.Succeeded)return Content(result.ErrorCode=="not_found"?HttpStatusCode.NotFound:result.ErrorCode=="validation"?HttpStatusCode.BadRequest:HttpStatusCode.Conflict,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));
            return Ok(ApiEnvelope<SaleCorrectionResult>.Ok(result));
        }
        private IHttpActionResult CancellationResponse(SaleCancellationResult result){if(!result.Succeeded)return Content(result.ErrorCode=="not_found"?HttpStatusCode.NotFound:result.ErrorCode=="validation"?HttpStatusCode.BadRequest:HttpStatusCode.Conflict,ApiEnvelope<object>.Fail(result.ErrorCode,result.ErrorMessage));return Ok(ApiEnvelope<SaleCancellationResult>.Ok(result));}
        private async Task<AuthCheck> Require(CancellationToken ct,bool write,string requiredScreen=null){var token=ReadCookie();var user=await _auth.GetCurrentUserAsync(token,ct).ConfigureAwait(false);if(user==null)return new AuthCheck{Error=Content(HttpStatusCode.Unauthorized,ApiEnvelope<object>.Fail("unauthorized","يرجى تسجيل الدخول أولًا."))};if(user.Role!=UserRoles.Administrator){var access=await _users.GetEffectiveAccessAsync(user,await _features.GetEffectiveAsync(ct).ConfigureAwait(false),ct).ConfigureAwait(false);if(requiredScreen!=null?!access.ScreenIds.Contains(requiredScreen,StringComparer.OrdinalIgnoreCase):!new[]{"pos","sales-management","sales-list","sales-returns"}.Any(x=>access.ScreenIds.Contains(x,StringComparer.OrdinalIgnoreCase)))return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("screen_forbidden","لا تملك صلاحية شاشة المبيعات."))};}if(write&&!await _auth.ValidateCsrfAsync(token,ReadHeader("X-CSRF-Token"),ct).ConfigureAwait(false))return new AuthCheck{Error=Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("invalid_csrf","تعذر التحقق من الطلب. حدّث الصفحة وحاول مرة أخرى."))};return new AuthCheck{User=user};}
        private static SaleDraftCommand Map(SaleRequest r){if(r==null)return null;return new SaleDraftCommand{ExpectedVersion=r.ExpectedVersion,PaymentAmount=r.PaymentAmount??r.Amount,HasPaymentAllocations=r.Payments!=null,CustomerId=r.CustomerId,WarehouseId=r.WarehouseId,InvoiceDate=r.InvoiceDate,PaymentMode=r.PaymentMode,ImmediateMethod=r.ImmediateMethod,PaymentInstrumentId=r.PaymentInstrumentId,Discount=r.Discount,Delivery=r.Delivery==null?null:new DeliveryDetails{DriverId=r.Delivery.DriverId,PrimaryPhone=r.Delivery.PrimaryPhone,SecondaryPhone=r.Delivery.SecondaryPhone,Address=r.Delivery.Address,Fee=r.Delivery.Fee,Notes=r.Delivery.Notes},Lines=(r.Lines??new List<SaleLineRequest>()).Select(x=>x==null?null:new SaleLine{VariantId=x.VariantId,ProductUnitId=x.ProductUnitId,Quantity=x.Quantity,UnitPrice=x.UnitPrice,ManualPriceOverride=x.ManualPriceOverride,Discount=x.Discount,BatchLotNumber=x.LotNumber,ExpirationDate=x.ExpirationDate,PhysicalUnits=x.PhysicalUnits??new List<SaleTrackingUnit>()}).ToList()};}
        private async Task<bool> CanUseManualPrices(UserIdentity user,IEnumerable<SaleLineRequest> lines,CancellationToken ct)
        {
            if(user.Role==UserRoles.Administrator)return true;
            var granted=await _users.CanEditSalePricesAsync(user,ct).ConfigureAwait(false);
            return granted||lines==null||!lines.Any(x=>x!=null&&x.ManualPriceOverride);
        }
        private IHttpActionResult PriceForbidden(){return Content(HttpStatusCode.Forbidden,ApiEnvelope<object>.Fail("sale_price_forbidden","لا تملك صلاحية تعديل سعر البيع أثناء الفاتورة."));}
        private IHttpActionResult Bad(string m){return Content(HttpStatusCode.BadRequest,ApiEnvelope<object>.Fail("validation",m));}private IHttpActionResult Conflict(string m){return Content(HttpStatusCode.Conflict,ApiEnvelope<object>.Fail("sales_conflict",m));}
        private string ReadCookie(){IEnumerable<string> values;if(!Request.Headers.TryGetValues("Cookie",out values))return null;foreach(var header in values)foreach(var part in header.Split(';')){var pair=part.Trim().Split(new[]{'='},2);if(pair.Length==2&&pair[0]=="alkhayal_session")return pair[1];}return null;}private string ReadHeader(string n){IEnumerable<string> values;return Request.Headers.TryGetValues(n,out values)?values.FirstOrDefault():null;}
        private sealed class AuthCheck{public UserIdentity User{get;set;}public IHttpActionResult Error{get;set;}}
    }
    public sealed class SaleRequest{public long? ExpectedVersion{get;set;}public string Amount{get;set;}public string PaymentAmount{get;set;}public List<SalePaymentRequest> Payments{get;set;}public string ImmediateMethod{get;set;}public long? PaymentInstrumentId{get;set;}public long? CustomerId{get;set;}public long WarehouseId{get;set;}public string InvoiceDate{get;set;}public string PaymentMode{get;set;}public string Discount{get;set;}public DeliveryRequest Delivery{get;set;}public List<SaleLineRequest> Lines{get;set;}}
    public sealed class InstallmentSalePostRequest{public SaleRequest Invoice{get;set;}public string DownPayment{get;set;}public string InterestRate{get;set;}public string InterestReturnPolicy{get;set;}public int InstallmentCount{get;set;}public int IntervalMonths{get;set;}public string FirstDueOn{get;set;}public long? TreasuryAccountId{get;set;}public string OperationKey{get;set;}}
    public sealed class DeliveryRequest{public long? DriverId{get;set;}public string PrimaryPhone{get;set;}public string SecondaryPhone{get;set;}public string Address{get;set;}public string Fee{get;set;}public string Notes{get;set;}}
    public sealed class SaleLineRequest{public long VariantId{get;set;}public long ProductUnitId{get;set;}public string Quantity{get;set;}public string UnitPrice{get;set;}public bool ManualPriceOverride{get;set;}public string Discount{get;set;}public string LotNumber{get;set;}public string ExpirationDate{get;set;}public List<SaleTrackingUnit> PhysicalUnits{get;set;}}
    public sealed class SalePostRequest{public string OperationKey{get;set;}}
    public sealed class SaleCancelRequest{public string OperationKey{get;set;}public string Reason{get;set;}}
    public sealed class SalePaymentRequest{public string Amount{get;set;}public string Method{get;set;}public long? TreasuryAccountId{get;set;}public string OperationKey{get;set;}public string OccurredOn{get;set;}public string Notes{get;set;}}
    public sealed class SaleReturnRequest{public string OperationKey{get;set;}public string Reason{get;set;}public string InterestSettlement{get;set;}public List<SaleReturnLineRequest> Lines{get;set;}}
    public sealed class SaleReturnLineRequest{public long InvoiceLineId{get;set;}public string Quantity{get;set;}public bool IsDamaged{get;set;}public List<string> Serials{get;set;}}
    public sealed class SaleCorrectionRequest{public long ExpectedVersion{get;set;}public string OperationKey{get;set;}public string SettlementKind{get;set;}public string SettlementMethod{get;set;}public long? TreasuryAccountId{get;set;}public SaleRequest Invoice{get;set;}}
}
