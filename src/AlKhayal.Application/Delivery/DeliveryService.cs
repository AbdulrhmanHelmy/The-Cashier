using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Delivery
{
    public sealed class Driver { public long Id { get; set; } public string Name { get; set; } public string Phone { get; set; } public bool IsActive { get; set; } }
    public sealed class DeliveryOrder { public long SalesInvoiceId { get; set; } public string InvoiceNumber { get; set; } public string InvoiceDate { get; set; } public string CustomerName { get; set; } public string PrimaryPhone { get; set; } public string Address { get; set; } public string DriverName { get; set; } public string Status { get; set; } public long FeeScaled { get; set; } public long TotalScaled { get; set; } }
    public sealed class DeliveryPage { public IReadOnlyList<DeliveryOrder> Items { get; set; } public long Total { get; set; } public int Page { get; set; } public int PageSize { get; set; } }
    public interface IDeliveryStore { Task<IReadOnlyList<Driver>> DriversAsync(string search,bool activeOnly,CancellationToken ct,int? limit=null,long? id=null); Task<Driver> SaveDriverAsync(Driver driver,CancellationToken ct); Task<DeliveryPage> OrdersAsync(string search,string status,long? driverId,int page,int pageSize,CancellationToken ct,long? invoiceId=null); Task SetStatusAsync(long invoiceId,string status,CancellationToken ct); }
    public sealed class DeliveryService
    {
        private readonly IDeliveryStore _store; public DeliveryService(IDeliveryStore store){_store=store;}
        public Task<IReadOnlyList<Driver>> DriversAsync(string search,bool activeOnly,CancellationToken ct,int? limit=null,long? id=null)=>_store.DriversAsync(search??"",activeOnly,ct,limit.HasValue?Math.Max(1,Math.Min(20,limit.Value)):(int?)null,id);
        public Task<DeliveryPage> OrdersAsync(string search,string status,long? driverId,int page,int pageSize,CancellationToken ct,long? invoiceId=null)=>_store.OrdersAsync(search??"",status??"",driverId,Math.Max(1,page),Math.Min(100,Math.Max(1,pageSize)),ct,invoiceId);
        public async Task<Driver> SaveDriverAsync(Driver driver,CancellationToken ct){if(driver==null||driver.Id<0||string.IsNullOrWhiteSpace(driver.Name)||driver.Name.Trim().Length>160||string.IsNullOrWhiteSpace(driver.Phone)||driver.Phone.Trim().Length<3||driver.Phone.Trim().Length>40)throw new ArgumentException("أدخل اسم الطيار ورقم هاتف صحيحين.");driver.Name=driver.Name.Trim();driver.Phone=driver.Phone.Trim();var saved=await _store.SaveDriverAsync(driver,ct).ConfigureAwait(false);if(saved==null)throw new ArgumentException("الطيار غير موجود.");return saved;}
        public Task SetStatusAsync(long invoiceId,string status,CancellationToken ct){if(invoiceId<=0||Array.IndexOf(new[]{"pending","out_for_delivery","delivered","failed"},status)<0)throw new ArgumentException("حالة التوصيل غير صالحة.");return _store.SetStatusAsync(invoiceId,status,ct);}
    }
}
