using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AlKhayal.Application.Catalog
{
    public class ScaleBarcodeProfile
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Prefix { get; set; }
        public int ProductLength { get; set; }
        public string ValueType { get; set; }
        public int ValueLength { get; set; }
        public int ValueDecimals { get; set; }
        public bool ValueHasCheckDigit { get; set; }
        public bool IsActive { get; set; }
        public long Version { get; set; }
    }

    public static class ScaleBarcodeRules
    {
        public static bool TryDecode(string barcode, ScaleBarcodeProfile profile, out string productCode, out decimal decodedValue)
        {
            productCode = null; decodedValue = 0;
            if (profile == null || !profile.IsActive || barcode == null || barcode.Length != 13 || barcode.Any(c => c < '0' || c > '9') || string.IsNullOrEmpty(profile.Prefix) || !barcode.StartsWith(profile.Prefix, StringComparison.Ordinal)) return false;
            if (barcode[12] != BarcodeRules.CheckDigit(barcode.Substring(0, 12))) return false;
            var dataLength = barcode.Length - 1;
            var valueStart = profile.Prefix.Length + profile.ProductLength;
            var numericValueLength = profile.ValueLength - (profile.ValueHasCheckDigit ? 1 : 0);
            if (profile.ProductLength < 1 || profile.ValueLength < 1 || profile.Prefix.Length + profile.ProductLength + profile.ValueLength != dataLength || numericValueLength < 1 || profile.ValueDecimals < 0 || profile.ValueDecimals > numericValueLength || profile.ValueType != "weight" && profile.ValueType != "price") return false;
            var raw = barcode.Substring(valueStart, numericValueLength);
            if (profile.ValueHasCheckDigit && !char.IsDigit(barcode[valueStart + numericValueLength])) return false;
            long scaled; if (!long.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out scaled) || scaled <= 0) return false;
            decodedValue = scaled / (decimal)Math.Pow(10, profile.ValueDecimals);
            productCode = barcode.Substring(profile.Prefix.Length, profile.ProductLength);
            return true;
        }
    }

    public sealed class SaveScaleBarcodeProfileCommand : ScaleBarcodeProfile { }

    public static class BarcodeRules
    {
        public static string Validate(string code, string format, bool completeEan = false)
        {
            // The existing lookup identity is FormKC + uppercase; retain the entered display value. Do not trim or normalize printed data silently.
            if (string.IsNullOrEmpty(code) || code.Length > 64 || code != code.Trim()) throw new ArgumentException("أدخل باركودًا دون مسافات طرفية وبحد أقصى 64 رمزًا.");
            if (format == "ean13")
            {
                if (!code.All(c => c >= '0' && c <= '9')) throw new ArgumentException("EAN-13 يقبل الأرقام الغربية فقط.");
                if (code.Length == 12 && completeEan) return code + CheckDigit(code);
                if (code.Length != 13 || code[12] != CheckDigit(code.Substring(0, 12))) throw new ArgumentException("EAN-13 يتطلب 13 رقمًا ورقم تحقق صحيحًا.");
            }
            else if (format != "code128" || code.Any(c => c < 32 || c > 126)) throw new ArgumentException("Code 128 يدعم النص الإنجليزي والأرقام والرموز ASCII القابلة للطباعة فقط.");
            return code;
        }
        public static char CheckDigit(string body)
        {
            if (body == null || body.Length != 12 || body.Any(c => c < '0' || c > '9')) throw new ArgumentException("أدخل 12 رقمًا لحساب رقم التحقق.");
            var sum = body.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum(); return (char)('0' + (10 - sum % 10) % 10);
        }
        public static string GenerateInternal()
        {
            var bytes = new byte[8]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return "29" + (BitConverter.ToUInt64(bytes, 0) % 100000000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    public sealed class BarcodeChange
    {
        public long ProductId { get; set; }
        public long VariantId { get; set; }
        public long ProductUnitId { get; set; }
        public long? ReplaceBarcodeId { get; set; }
        public long Version { get; set; }
        public string Code { get; set; }
        public string Format { get; set; } = "code128";
        public bool Generate { get; set; }
        public string OperationKey { get; set; }
    }
    public sealed class BarcodeChangeResult
    {
        public long Id { get; set; }
        public string Code { get; set; }
        public string Format { get; set; }
        public long Version { get; set; }
        public bool Replayed { get; set; }
    }
    public sealed class BarcodeDelete
    {
        public long ProductId { get; set; }
        public long BarcodeId { get; set; }
        public long Version { get; set; }
        public string OperationKey { get; set; }
    }
    public sealed class BarcodeDeleteResult
    {
        public long BarcodeId { get; set; }
        public string Code { get; set; }
        public long Version { get; set; }
        public bool Replayed { get; set; }
    }
    public interface IProductBarcodeStore
    {
        Task<BarcodeChangeResult> ChangeBarcodeAsync(BarcodeChange command, long userId, CancellationToken ct);
        Task<BarcodeDeleteResult> DeleteBarcodeAsync(BarcodeDelete command, long userId, CancellationToken ct);
    }
    public sealed class BarcodeService
    {
        private readonly IProductBarcodeStore _store;
        public BarcodeService(IProductBarcodeStore store) { _store = store; }
        public Task<BarcodeChangeResult> SaveAsync(BarcodeChange x, long actorId, CancellationToken ct)
        {
            if (x == null || x.ProductId <= 0 || x.VariantId <= 0 || x.ProductUnitId <= 0 || x.Version < 1 || string.IsNullOrWhiteSpace(x.OperationKey) || x.OperationKey.Length > 100 || x.Generate && (!string.IsNullOrEmpty(x.Code) || x.Format != "code128")) throw new ArgumentException("راجع المنتج والمتغير والوحدة وإصدار البيانات ومفتاح العملية.");
            if (!x.Generate) x.Code = BarcodeRules.Validate(x.Code, x.Format, true);
            return _store.ChangeBarcodeAsync(x, actorId, ct);
        }
        public Task<BarcodeDeleteResult> DeleteAsync(BarcodeDelete x, long actorId, CancellationToken ct)
        {
            if (x == null || x.ProductId <= 0 || x.BarcodeId <= 0 || x.Version < 1 || string.IsNullOrWhiteSpace(x.OperationKey) || x.OperationKey.Length > 100)
                throw new ArgumentException("راجع المنتج والباركود وإصدار البيانات ومفتاح العملية.");
            return _store.DeleteBarcodeAsync(x, actorId, ct);
        }
    }
}
