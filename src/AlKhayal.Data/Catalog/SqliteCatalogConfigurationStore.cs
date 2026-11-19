using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using Dapper;

namespace AlKhayal.Data.Catalog
{
    public sealed partial class SqliteCatalogStore : ICatalogStore
    {
        private static readonly HashSet<string> AllowedCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "product-variants", "multiple-units", "weight-based-products",
            "product-barcode-settings", "barcode-labels", "wholesale-pricing", "inventory", "sales",
            "serial-numbers", "imei-tracking", "batch-tracking", "expiry-dates", "warranty-tracking"
        };

        private static readonly Regex CustomKeyRegex = new Regex("^[A-Z][A-Z0-9_-]{1,31}$", RegexOptions.Compiled);

        public async Task<CatalogProductDetailRecord> GetProductDetailAsync(long productId, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var product = await connection.QuerySingleOrDefaultAsync<CatalogProductRecord>(
                    new CommandDefinition(ProductSelect + " WHERE p.Id=@ProductId", new { ProductId = productId }, cancellationToken: ct)).ConfigureAwait(false);
                if (product == null) return null;

                var capabilities = (await connection.QueryAsync<ProductCapabilityAssignmentRecord>(
                    new CommandDefinition("SELECT CapabilityKey, IsEnabled, AssignedUtc, UpdatedUtc, ChangedByUserId FROM ProductCapabilityAssignments WHERE ProductId=@ProductId ORDER BY CapabilityKey",
                    new { ProductId = productId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                var units = (await connection.QueryAsync<ProductUnitRecord>(
                    new CommandDefinition(@"SELECT u.Id, u.ProductId, u.UnitId, un.Code, un.Name, un.Symbol,
                        u.ToBaseNumerator, u.ToBaseDenominator, u.SellPriceScaled, u.UnitPurchasePriceScaled, u.IsBase, u.IsSaleUnit, u.IsPurchaseUnit, u.IsActive,
                        u.FirstUsedUtc, u.Version,
                        CASE WHEN u.FirstUsedUtc IS NOT NULL OR EXISTS (SELECT 1 FROM ProductBarcodes pb WHERE pb.ProductUnitId=u.Id) THEN 1 ELSE 0 END AS IsLocked
                        FROM ProductUnits u JOIN Units un ON un.Id=u.UnitId
                        WHERE u.ProductId=@ProductId ORDER BY u.IsBase DESC, u.Id",
                    new { ProductId = productId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                var attrRows = (await connection.QueryAsync<ProductAttributeAssignmentRecord>(
                    new CommandDefinition(@"SELECT a.AttributeId, pa.AttributeKey Key, pa.Name, pa.Kind, a.SortOrder
                        FROM ProductAttributeAssignments a JOIN ProductAttributes pa ON pa.Id=a.AttributeId
                        WHERE a.ProductId=@ProductId ORDER BY a.SortOrder, a.AttributeId",
                    new { ProductId = productId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                foreach (var attr in attrRows)
                {
                    var values = (await connection.QueryAsync<ProductAttributeValueAssignmentRecord>(
                        new CommandDefinition(@"SELECT va.AttributeValueId ValueId, pav.Value, va.SortOrder
                            FROM ProductAttributeValueAssignments va JOIN ProductAttributeValues pav ON pav.Id=va.AttributeValueId
                            WHERE va.ProductId=@ProductId AND va.AttributeId=@AttributeId ORDER BY va.SortOrder, va.AttributeValueId",
                        new { ProductId = productId, AttributeId = attr.AttributeId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    attr.Values = values;
                }

                var variants = (await connection.QueryAsync<ProductVariantRecord>(
                    new CommandDefinition(@"SELECT Id, ProductId, Name, NormalizedName, IsDefault, IsActive, CombinationKey,
                        SellPriceOverrideScaled, PurchasePriceOverrideScaled, ArchivedUtc, Version
                        FROM ProductVariants WHERE ProductId=@ProductId ORDER BY IsDefault DESC, Id",
                    new { ProductId = productId }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                var bundleComponents=(await connection.QueryAsync<BundleComponentRecord>(new CommandDefinition(@"
                    SELECT cv.ProductId,bc.ComponentVariantId VariantId,cp.Name ProductName,cv.Name VariantName,u.Name UnitName,u.Symbol UnitSymbol,bc.QuantityBaseScaled
                    FROM ProductBundleComponents bc JOIN ProductVariants cv ON cv.Id=bc.ComponentVariantId
                    JOIN Products cp ON cp.Id=cv.ProductId JOIN ProductDetails cd ON cd.ProductId=cp.Id
                    JOIN Units u ON u.Id=cd.BaseUnitId WHERE bc.BundleProductId=@ProductId ORDER BY cp.Name,cv.Name",
                    new{ProductId=productId},cancellationToken:ct)).ConfigureAwait(false)).ToList();

                foreach (var variant in variants)
                {
                    var valIds = (await connection.QueryAsync<long>(
                        new CommandDefinition("SELECT AttributeValueId FROM ProductVariantValues WHERE VariantId=@VariantId ORDER BY AttributeId",
                        new { VariantId = variant.Id }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                    variant.AttributeValueIds = valIds;

                    variant.Sku = await connection.QuerySingleOrDefaultAsync<ProductSkuRecord>(
                        new CommandDefinition("SELECT Id, ProductId, VariantId, Sku, NormalizedSku, RetiredUtc, RetiredByUserId, CreatedUtc, CreatedByUserId FROM ProductSkus WHERE VariantId=@VariantId AND RetiredUtc IS NULL LIMIT 1",
                        new { VariantId = variant.Id }, cancellationToken: ct)).ConfigureAwait(false);

                    variant.Barcodes = (await connection.QueryAsync<ProductBarcodeRecord>(
                        new CommandDefinition("SELECT Id, ProductId, VariantId, ProductUnitId, Barcode, NormalizedBarcode, BarcodeType, IsPrimary, RetiredUtc, RetiredByUserId, CreatedUtc, CreatedByUserId, COALESCE((SELECT Format FROM BarcodeSymbologies WHERE BarcodeId=ProductBarcodes.Id),'code128') Format FROM ProductBarcodes WHERE VariantId=@VariantId ORDER BY IsPrimary DESC, Id",
                        new { VariantId = variant.Id }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                }

                return new CatalogProductDetailRecord
                {
                    Product = product,
                    VariantMode = product.VariantMode,
                    Capabilities = capabilities,
                    Attributes = attrRows,
                    Units = units,
                    Variants = variants
                    ,BundleComponents=bundleComponents
                };
            }
        }

        public async Task<IReadOnlyList<ProductAttributeRecord>> ListAttributesAsync(string kind, bool includeInactive, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var where = new List<string> { "Kind='custom'" };
                if (!string.IsNullOrWhiteSpace(kind)) where.Add("Kind=@Kind");
                if (!includeInactive) where.Add("IsActive=1");
                var predicate = where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where);

                var attributes = (await connection.QueryAsync<ProductAttributeRecord>(
                    new CommandDefinition("SELECT Id, AttributeKey, NormalizedKey, Name, Kind, IsSystem, IsActive, SortOrder, Version FROM ProductAttributes" + predicate + " ORDER BY Kind, SortOrder, Name",
                    new { Kind = kind }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                foreach (var attr in attributes)
                {
                    var valWhere = " WHERE AttributeId=@AttributeId" + (includeInactive ? "" : " AND IsActive=1");
                    attr.Values = (await connection.QueryAsync<ProductAttributeValueRecord>(
                        new CommandDefinition("SELECT Id, AttributeId, Value, NormalizedValue, SortOrder, IsActive, Version FROM ProductAttributeValues" + valWhere + " ORDER BY SortOrder, Value",
                        new { AttributeId = attr.Id }, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                }

                return attributes;
            }
        }

        public async Task<ProductAttributeRecord> GetAttributeAsync(long id, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                var attr = await connection.QuerySingleOrDefaultAsync<ProductAttributeRecord>(
                    new CommandDefinition("SELECT Id, AttributeKey, NormalizedKey, Name, Kind, IsSystem, IsActive, SortOrder, Version FROM ProductAttributes WHERE Id=@Id AND Kind='custom'",
                    new { Id = id }, cancellationToken: ct)).ConfigureAwait(false);
                if (attr == null) return null;

                attr.Values = (await connection.QueryAsync<ProductAttributeValueRecord>(
                    new CommandDefinition("SELECT Id, AttributeId, Value, NormalizedValue, SortOrder, IsActive, Version FROM ProductAttributeValues WHERE AttributeId=@Id ORDER BY SortOrder, Value",
                    new { Id = id }, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                return attr;
            }
        }

        public async Task<ProductAttributeRecord> CreateAttributeAsync(CreateAttributeRecord record, CancellationToken ct)
        {
            if (record.Kind != "custom") throw Error("validation", "لا يمكن إنشاء سمات نظامية جديدة.");
            var key = NormalizeKey(record.AttributeKey);
            if (!CustomKeyRegex.IsMatch(key)) throw Error("validation", "كود السمة غير صالح.");
            var name = NormalizeText(record.Name, 1, 80, "اسم السمة");

            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var dup = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition("SELECT COUNT(*) FROM ProductAttributes WHERE NormalizedKey=@Key", new { Key = key }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (dup > 0) throw Error("duplicate_attribute", "كود السمة مستخدم بالفعل.");

                        var now = UtcNow();
                        var id = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition(@"INSERT INTO ProductAttributes (AttributeKey, NormalizedKey, Name, Kind, IsSystem, IsActive, SortOrder, Version, CreatedUtc, UpdatedUtc)
                                VALUES (@Key, @Key, @Name, 'custom', 0, @IsActive, @SortOrder, 1, @Now, @Now);
                                SELECT last_insert_rowid();",
                            new { Key = key, Name = name, record.IsActive, record.SortOrder, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                        await AuditAsync(connection, transaction, null, "attribute", id, "create",
                            SimpleJson(new Dictionary<string, object> { { "id", id }, { "key", key }, { "name", name } }), record.UserId, now, ct).ConfigureAwait(false);

                        transaction.Commit();
                        return await GetAttributeAsync(id, ct).ConfigureAwait(false);
                    }
                    catch (SQLiteException ex) when (IsUniqueViolation(ex))
                    {
                        throw Error("duplicate_attribute", "كود السمة مستخدم بالفعل.");
                    }
                }
            }
        }

        public async Task<ProductAttributeRecord> UpdateAttributeAsync(UpdateAttributeRecord record, CancellationToken ct)
        {
            var name = NormalizeText(record.Name, 1, 80, "اسم السمة");
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    var existing = await connection.QuerySingleOrDefaultAsync<ProductAttributeRecord>(
                        new CommandDefinition("SELECT Id, AttributeKey, NormalizedKey, Name, Kind, IsSystem, IsActive, SortOrder, Version FROM ProductAttributes WHERE Id=@Id AND Kind='custom'",
                        new { record.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    if (existing == null) throw Error("not_found", "السمة غير موجودة.");

                    if (existing.IsSystem && !record.IsActive)
                        throw Error("attribute_in_use", "لا يمكن تعطيل سمات النظام.");

                    if (!record.IsActive && existing.IsActive)
                    {
                        var inUse = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition("SELECT COUNT(*) FROM ProductAttributeAssignments WHERE AttributeId=@Id",
                            new { record.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (inUse > 0) throw Error("attribute_in_use", "لا يمكن تعطيل سمة مستخدمة في منتجات.");
                    }

                    var now = UtcNow();
                    var affected = await connection.ExecuteAsync(
                        new CommandDefinition("UPDATE ProductAttributes SET Name=@Name, SortOrder=@SortOrder, IsActive=@IsActive, Version=Version+1, UpdatedUtc=@Now WHERE Id=@Id AND Version=@Version",
                        new { Name = name, record.SortOrder, record.IsActive, Now = now, record.Id, record.Version }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                    if (affected == 0)
                        throw Error("version_conflict", "عُدلت بيانات السمة في جلسة أخرى. أعد التحميل ثم حاول مرة أخرى.");

                    await AuditAsync(connection, transaction, null, "attribute", record.Id, "update",
                        SimpleJson(new Dictionary<string, object> { { "id", record.Id }, { "name", name }, { "isActive", record.IsActive } }), record.UserId, now, ct).ConfigureAwait(false);

                    transaction.Commit();
                    return await GetAttributeAsync(record.Id, ct).ConfigureAwait(false);
                }
            }
        }

        public async Task<ProductAttributeValueRecord> GetAttributeValueAsync(long id, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                return await connection.QuerySingleOrDefaultAsync<ProductAttributeValueRecord>(
                    new CommandDefinition("SELECT Id, AttributeId, Value, NormalizedValue, SortOrder, IsActive, Version FROM ProductAttributeValues WHERE Id=@Id AND AttributeId IN (SELECT Id FROM ProductAttributes WHERE Kind='custom')",
                    new { Id = id }, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        public async Task<ProductAttributeValueRecord> CreateAttributeValueAsync(CreateAttributeValueRecord record, CancellationToken ct)
        {
            var value = NormalizeText(record.Value, 1, 80, "قيمة السمة");
            var normValue = NormalizeFormKc(value);


            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var attr = await connection.QuerySingleOrDefaultAsync<ProductAttributeRecord>(
                            new CommandDefinition("SELECT Id, Kind FROM ProductAttributes WHERE Id=@Id AND Kind='custom'", new { Id = record.AttributeId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (attr == null) throw Error("not_found", "السمة غير موجودة.");
                        if (attr?.Kind != "custom") throw Error("attribute_not_available", "السمة غير متاحة للتعديل.");

                        var dup = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition("SELECT COUNT(*) FROM ProductAttributeValues WHERE AttributeId=@AttributeId AND NormalizedValue=@Norm",
                            new { record.AttributeId, Norm = normValue }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (dup > 0) throw Error("duplicate_attribute_value", "قيمة السمة مستخدمة بالفعل.");

                        var now = UtcNow();
                        var id = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition(@"INSERT INTO ProductAttributeValues (AttributeId, Value, NormalizedValue, SortOrder, IsActive, Version, CreatedUtc, UpdatedUtc)
                                VALUES (@AttributeId, @Value, @Norm, @SortOrder, @IsActive, 1, @Now, @Now);
                                SELECT last_insert_rowid();",
                            new { record.AttributeId, Value = value, Norm = normValue, record.SortOrder, record.IsActive, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                        await AuditAsync(connection, transaction, null, "attribute_value", id, "create",
                            SimpleJson(new Dictionary<string, object> { { "id", id }, { "attributeId", record.AttributeId }, { "value", value } }), record.UserId, now, ct).ConfigureAwait(false);

                        transaction.Commit();
                        return await GetAttributeValueAsync(id, ct).ConfigureAwait(false);
                    }
                    catch (SQLiteException ex) when (IsUniqueViolation(ex))
                    {
                        throw Error("duplicate_attribute_value", "قيمة السمة مستخدمة بالفعل.");
                    }
                }
            }
        }

        public async Task<ProductAttributeValueRecord> UpdateAttributeValueAsync(UpdateAttributeValueRecord record, CancellationToken ct)
        {
            var value = NormalizeText(record.Value, 1, 80, "قيمة السمة");
            var normValue = NormalizeFormKc(value);


            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var existing = await connection.QuerySingleOrDefaultAsync<ProductAttributeValueRecord>(
                            new CommandDefinition("SELECT Id, AttributeId, Value, NormalizedValue, SortOrder, IsActive, Version FROM ProductAttributeValues WHERE Id=@Id AND AttributeId IN (SELECT Id FROM ProductAttributes WHERE Kind='custom')",
                            new { record.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (existing == null) throw Error("not_found", "قيمة السمة غير موجودة.");

                        var attr = await connection.QuerySingleOrDefaultAsync<ProductAttributeRecord>(
                            new CommandDefinition("SELECT Id, Kind FROM ProductAttributes WHERE Id=@Id AND Kind='custom'", new { Id = existing.AttributeId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (attr?.Kind != "custom") throw Error("attribute_not_available", "السمة غير متاحة للتعديل.");

                        var dup = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition("SELECT COUNT(*) FROM ProductAttributeValues WHERE AttributeId=@AttributeId AND NormalizedValue=@Norm AND Id<>@Id",
                            new { existing.AttributeId, Norm = normValue, record.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (dup > 0) throw Error("duplicate_attribute_value", "قيمة السمة مستخدمة بالفعل.");

                        if (!record.IsActive && existing.IsActive)
                        {
                            var inUse = await connection.ExecuteScalarAsync<long>(
                                new CommandDefinition(@"SELECT COUNT(*) FROM ProductVariantValues vv
                                    JOIN ProductVariants v ON v.Id=vv.VariantId
                                    WHERE vv.AttributeValueId=@Id AND v.ArchivedUtc IS NULL",
                                new { record.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                            if (inUse > 0) throw Error("attribute_value_in_use", "لا يمكن تعطيل قيمة مستخدمة في متغيرات غير مؤرشفة.");
                        }

                        var now = UtcNow();
                        var affected = await connection.ExecuteAsync(
                            new CommandDefinition("UPDATE ProductAttributeValues SET Value=@Value, NormalizedValue=@Norm, SortOrder=@SortOrder, IsActive=@IsActive, Version=Version+1, UpdatedUtc=@Now WHERE Id=@Id AND Version=@Version",
                            new { Value = value, Norm = normValue, record.SortOrder, record.IsActive, Now = now, record.Id, record.Version }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                        if (affected == 0)
                            throw Error("version_conflict", "عُدلت بيانات قيمة السمة في جلسة أخرى. أعد التحميل ثم حاول مرة أخرى.");

                        await AuditAsync(connection, transaction, null, "attribute_value", record.Id, "update",
                            SimpleJson(new Dictionary<string, object> { { "id", record.Id }, { "value", value }, { "isActive", record.IsActive } }), record.UserId, now, ct).ConfigureAwait(false);

                        transaction.Commit();
                        return await GetAttributeValueAsync(record.Id, ct).ConfigureAwait(false);
                    }
                    catch (SQLiteException ex) when (IsUniqueViolation(ex))
                    {
                        throw Error("duplicate_attribute_value", "قيمة السمة مستخدمة بالفعل.");
                    }
                }
            }
        }

        public async Task<BarcodeLookupRecord> LookupBarcodeAsync(string normalizedBarcode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(normalizedBarcode)) throw Error("validation", "رمز الباركود فارغ.");
            var code = NormalizeFormKc(normalizedBarcode.Trim());

            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                const string query = @"SELECT
                    b.Id AS BarcodeId, b.Barcode AS Code, b.BarcodeType AS Type,
                    CASE WHEN b.RetiredUtc IS NOT NULL THEN 1 ELSE 0 END AS IsBarcodeRetired,
                    p.Id AS ProductId, p.Name AS ProductName, d.IsActive AS IsProductActive, d.ArchivedUtc AS ProductArchivedUtc,
                    v.Id AS VariantId, v.Name AS VariantName, v.IsDefault AS IsDefaultVariant, v.IsActive AS IsVariantActive, v.ArchivedUtc AS VariantArchivedUtc,
                    u.Id AS ProductUnitId, un.Id AS UnitId, un.Name AS UnitName, un.Symbol AS UnitSymbol, u.IsActive AS IsUnitActive,
                    u.ToBaseNumerator AS Numerator, u.ToBaseDenominator AS Denominator,
                    COALESCE(v.SellPriceOverrideScaled, d.SellPriceScaled) AS EffectiveSalePriceScaled,
                    COALESCE(v.PurchasePriceOverrideScaled, d.PurchasePriceScaled) AS EffectivePurchasePriceScaled
                    FROM ProductBarcodes b
                    JOIN ProductVariants v ON v.Id=b.VariantId
                    JOIN ProductDetails d ON d.ProductId=b.ProductId
                    JOIN Products p ON p.Id=b.ProductId
                    JOIN ProductUnits u ON u.Id=b.ProductUnitId
                    JOIN Units un ON un.Id=u.UnitId
                    WHERE b.NormalizedBarcode=@Code";

                return await connection.QuerySingleOrDefaultAsync<BarcodeLookupRecord>(
                    new CommandDefinition(query, new { Code = code }, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<ScaleBarcodeProfile>> ListScaleBarcodeProfilesAsync(CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
                return (await connection.QueryAsync<ScaleBarcodeProfile>(new CommandDefinition(@"SELECT Id,Name,Prefix,ProductLength,ValueType,ValueLength,ValueDecimals,ValueHasCheckDigit,IsActive,Version FROM ScaleBarcodeProfiles ORDER BY length(Prefix) DESC,Prefix,Id", cancellationToken: ct)).ConfigureAwait(false)).ToList();
        }

        public async Task<ScaleBarcodeProfile> SaveScaleBarcodeProfileAsync(SaveScaleBarcodeProfileCommand command, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        if (command.Id <= 0)
                        {
                            await connection.ExecuteAsync(new CommandDefinition(@"INSERT INTO ScaleBarcodeProfiles(Name,Prefix,ProductLength,ValueType,ValueLength,ValueDecimals,ValueHasCheckDigit,IsActive,Version,CreatedUtc,UpdatedUtc) VALUES(@Name,@Prefix,@ProductLength,@ValueType,@ValueLength,@ValueDecimals,@ValueHasCheckDigit,@IsActive,1,@Now,@Now)", new { command.Name, command.Prefix, command.ProductLength, command.ValueType, command.ValueLength, command.ValueDecimals, command.ValueHasCheckDigit, command.IsActive, Now=now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                            command.Id = connection.LastInsertRowId;
                            command.Version = 1;
                        }
                        else
                        {
                            var changed = await connection.ExecuteAsync(new CommandDefinition(@"UPDATE ScaleBarcodeProfiles SET Name=@Name,Prefix=@Prefix,ProductLength=@ProductLength,ValueType=@ValueType,ValueLength=@ValueLength,ValueDecimals=@ValueDecimals,ValueHasCheckDigit=@ValueHasCheckDigit,IsActive=@IsActive,Version=Version+1,UpdatedUtc=@Now WHERE Id=@Id AND Version=@ExpectedVersion", new { command.Name, command.Prefix, command.ProductLength, command.ValueType, command.ValueLength, command.ValueDecimals, command.ValueHasCheckDigit, command.IsActive, Now=now, command.Id, ExpectedVersion=command.Version }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                            if (changed == 0) throw Error("version_conflict", "تم تعديل ملف الميزان أو حذفه؛ حدّث القائمة ثم أعد المحاولة.");
                            command.Version++;
                        }
                        var saved = await connection.QuerySingleAsync<ScaleBarcodeProfile>(new CommandDefinition("SELECT Id,Name,Prefix,ProductLength,ValueType,ValueLength,ValueDecimals,ValueHasCheckDigit,IsActive,Version FROM ScaleBarcodeProfiles WHERE Id=@Id", new { command.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        transaction.Commit(); return saved;
                    }
                    catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Constraint && ex.Message.IndexOf("scale_profile_exists", StringComparison.OrdinalIgnoreCase) >= 0)
                    { throw Error("scale_profile_exists", "تم إعداد ميزان بالفعل. يمكنك تعديل إعداداته الحالية فقط."); }
                    catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Constraint)
                    { throw Error("duplicate_scale_prefix", "يوجد ملف ميزان آخر يستخدم البادئة نفسها."); }
                }
            }
        }

        public async Task<BarcodeLookupRecord> LookupScaleProductAsync(string productCode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(productCode)) return null;
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                return await connection.QuerySingleOrDefaultAsync<BarcodeLookupRecord>(new CommandDefinition(@"SELECT 0 BarcodeId,s.Sku Code,'scale_weight' Type,0 IsBarcodeRetired,
                    p.Id ProductId,p.Name ProductName,d.IsActive IsProductActive,d.ArchivedUtc ProductArchivedUtc,
                    v.Id VariantId,v.Name VariantName,v.IsDefault IsDefaultVariant,v.IsActive IsVariantActive,v.ArchivedUtc VariantArchivedUtc,
                    u.Id ProductUnitId,un.Id UnitId,un.Name UnitName,un.Symbol UnitSymbol,u.IsActive IsUnitActive,
                    u.ToBaseNumerator Numerator,u.ToBaseDenominator Denominator,
                    COALESCE(v.SellPriceOverrideScaled,d.SellPriceScaled) EffectiveSalePriceScaled,
                    COALESCE(v.PurchasePriceOverrideScaled,d.PurchasePriceScaled) EffectivePurchasePriceScaled
                    FROM ProductSkus s JOIN ProductVariants v ON v.Id=s.VariantId JOIN ProductDetails d ON d.ProductId=s.ProductId JOIN Products p ON p.Id=s.ProductId
                    JOIN ProductUnits u ON u.ProductId=p.Id AND u.IsBase=1 JOIN Units un ON un.Id=u.UnitId
                    WHERE s.NormalizedSku=@Code AND s.RetiredUtc IS NULL AND d.SellingType='weight'
                    ORDER BY v.IsDefault DESC,v.Id LIMIT 1", new { Code = productCode }, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        public async Task<CatalogProductRecord> CreateAggregateProductAsync(CreateProductRecord record, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var operation = await connection.QuerySingleOrDefaultAsync<OperationRow>(
                            new CommandDefinition("SELECT RequestHash, ProductId FROM CatalogOperations WHERE OperationKey=@OperationKey",
                            new { record.OperationKey }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (operation != null)
                        {
                            if (!string.Equals(operation.RequestHash, record.RequestHash, StringComparison.Ordinal))
                                throw Error("operation_key_conflict", "مفتاح العملية مستخدم لطلب مختلف.");
                            transaction.Commit();
                            return await FindProductAsync(operation.ProductId, ct).ConfigureAwait(false);
                        }

                        await ValidateReferences(connection, transaction, record.CategoryId, record.BaseUnitId, ct).ConfigureAwait(false);


                        var config = record.Configuration;
                        var variantMode = config != null ? config.VariantMode : record.VariantMode;
                        ValidateCapabilitiesList(config?.CapabilityKeys);
                        if (record.SellingType == "weight" && config?.Units != null && config.Units.Any(u => !u.IsBase && u.IsActive))
                            throw Error("weight_unit_conversion", "البيع بالوزن يستخدم الكمية الأساسية بالكيلوجرام دون تحويلات وحدات إضافية.");
                        ValidateUnitConfiguration(config?.Units, record.BaseUnitId);

                        var now = UtcNow();
                        var legacyPrice = record.SellPriceScaled / (decimal)CatalogService.MoneyScale;

                        var productId = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition("INSERT INTO Products (Name, Price) VALUES (@Name, @Price); SELECT last_insert_rowid();",
                            new { record.Name, Price = legacyPrice }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                        await connection.ExecuteAsync(new CommandDefinition(@"INSERT INTO ProductDetails
                            (ProductId, Description, CategoryId, BaseUnitId, ProductType, SellingType, SellPriceScaled, PurchasePriceScaled, ReorderPointScaled, AllowSaleWhenOutOfStock, MoneyScale, IsActive, ArchivedUtc, MigrationState, VariantMode, Version, CreatedUtc, UpdatedUtc)
                            VALUES (@ProductId, @Description, @CategoryId, @BaseUnitId, @ProductType, @SellingType, @SellPriceScaled, @PurchasePriceScaled, COALESCE(@ReorderPointScaled,0), @AllowSaleWhenOutOfStock, 4, @IsActive, NULL, 'ready', @VariantMode, 1, @Now, @Now);
                            INSERT INTO CatalogOperations (OperationKey, RequestHash, OperationType, ProductId, CreatedUtc)
                            VALUES (@OperationKey, @RequestHash, 'create_product', @ProductId, @Now);",
                            new { ProductId = productId, record.Description, record.CategoryId, record.BaseUnitId, record.ProductType, SellingType=record.SellingType??"piece", record.SellPriceScaled, record.PurchasePriceScaled, record.ReorderPointScaled, AllowSaleWhenOutOfStock=record.AllowSaleWhenOutOfStock?1:0, record.IsActive, VariantMode = variantMode ? 1 : 0, record.OperationKey, record.RequestHash, Now = now },
                            transaction, cancellationToken: ct)).ConfigureAwait(false);

                        var defaultVariantId = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition("INSERT INTO ProductVariants (ProductId, Name, NormalizedName, IsDefault, IsActive, CreatedUtc, UpdatedUtc) VALUES (@ProductId, 'افتراضي', 'DEFAULT', 1, @IsActive, @Now, @Now); SELECT last_insert_rowid();",
                            new { ProductId = productId, record.IsActive, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                        if (config?.CapabilityKeys != null)
                        {
                            foreach (var key in config.CapabilityKeys.Distinct(StringComparer.OrdinalIgnoreCase))
                            {
                                await connection.ExecuteAsync(new CommandDefinition(
                                    "INSERT INTO ProductCapabilityAssignments (ProductId, CapabilityKey, IsEnabled, AssignedUtc, UpdatedUtc, ChangedByUserId) VALUES (@ProductId, @Key, 1, @Now, @Now, @UserId)",
                                    new { ProductId = productId, Key = key.ToLowerInvariant(), Now = now, UserId = record.UserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                            }
                        }

                        var unitIdMap = new Dictionary<long, long>();
                        long baseProductUnitId = 0;
                        if (config?.Units != null && config.Units.Count > 0)
                        {
                            var baseUnitConfig = config.Units.FirstOrDefault(u => u.IsBase);
                            if (baseUnitConfig == null || baseUnitConfig.UnitId != record.BaseUnitId)
                                throw Error("validation", "يجب تحديد وحدة أساسية مطابقة لوحدة المنتج الأساسية.");

                            baseProductUnitId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                                @"INSERT INTO ProductUnits (ProductId, UnitId, ToBaseNumerator, ToBaseDenominator, SellPriceScaled, UnitPurchasePriceScaled, IsBase, IsSaleUnit, IsPurchaseUnit, IsActive, FirstUsedUtc, Version, CreatedUtc, UpdatedUtc)
                                VALUES (@ProductId, @UnitId, 1, 1, @SellPriceScaled, @UnitPurchasePriceScaled, 1, @IsSaleUnit, @IsPurchaseUnit, @IsActive, NULL, 1, @Now, @Now); SELECT last_insert_rowid();",
                                new { ProductId = productId, UnitId = record.BaseUnitId, baseUnitConfig.SellPriceScaled, baseUnitConfig.UnitPurchasePriceScaled, baseUnitConfig.IsSaleUnit, baseUnitConfig.IsPurchaseUnit, baseUnitConfig.IsActive, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                            unitIdMap[record.BaseUnitId] = baseProductUnitId;
                            if (baseUnitConfig.Id.HasValue) unitIdMap[baseUnitConfig.Id.Value] = baseProductUnitId;

                            foreach (var u in config.Units.Where(u => !u.IsBase))
                            {
                                ValidateRatio(u.ToBaseNumerator, u.ToBaseDenominator);
                                if (!u.IsSaleUnit && !u.IsPurchaseUnit) throw Error("validation", "يجب أن تكون الوحدة مخصصة للبيع أو الشراء على الأقل.");
                                var unitRowId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                                    @"INSERT INTO ProductUnits (ProductId, UnitId, ToBaseNumerator, ToBaseDenominator, SellPriceScaled, UnitPurchasePriceScaled, IsBase, IsSaleUnit, IsPurchaseUnit, IsActive, FirstUsedUtc, Version, CreatedUtc, UpdatedUtc)
                                    VALUES (@ProductId, @UnitId, @Num, @Den, @SellPriceScaled, @UnitPurchasePriceScaled, 0, @IsSaleUnit, @IsPurchaseUnit, @IsActive, NULL, 1, @Now, @Now); SELECT last_insert_rowid();",
                                    new { ProductId = productId, u.UnitId, Num = u.ToBaseNumerator, Den = u.ToBaseDenominator, u.SellPriceScaled, u.UnitPurchasePriceScaled, u.IsSaleUnit, u.IsPurchaseUnit, u.IsActive, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                unitIdMap[u.UnitId] = unitRowId;
                                if (u.Id.HasValue) unitIdMap[u.Id.Value] = unitRowId;
                            }
                        }
                        else
                        {
                            baseProductUnitId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                                @"INSERT INTO ProductUnits (ProductId, UnitId, ToBaseNumerator, ToBaseDenominator, IsBase, IsSaleUnit, IsPurchaseUnit, IsActive, FirstUsedUtc, Version, CreatedUtc, UpdatedUtc)
                                VALUES (@ProductId, @UnitId, 1, 1, 1, 1, 1, 1, NULL, 1, @Now, @Now); SELECT last_insert_rowid();",
                                new { ProductId = productId, UnitId = record.BaseUnitId, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                            unitIdMap[record.BaseUnitId] = baseProductUnitId;
                        }

                        await ValidateAttributeConfigurationAsync(connection, transaction, config, ct).ConfigureAwait(false);
                        var valToAttrMap = new Dictionary<long, long>();
                        if (variantMode && config?.Attributes != null)
                        {
                            if (config.Attributes.Count < 1 || config.Attributes.Count > 3)
                                throw Error("validation", "يجب اختيار بين سمة واحدة و 3 سمات للمنتج ذي المتغيرات.");

                            long totalCombinations = 1;
                            foreach (var a in config.Attributes)
                            {
                                if (a.ValueIds == null || a.ValueIds.Count < 1 || a.ValueIds.Count > 50)
                                    throw Error("validation", "يجب اختيار بين قيمة واحدة و 50 قيمة لكل سمة.");
                                totalCombinations *= a.ValueIds.Count;

                                await connection.ExecuteAsync(new CommandDefinition(
                                    "INSERT INTO ProductAttributeAssignments (ProductId, AttributeId, SortOrder, CreatedUtc, UpdatedUtc) VALUES (@ProductId, @AttributeId, @SortOrder, @Now, @Now)",
                                    new { ProductId = productId, a.AttributeId, a.SortOrder, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                                int valSort = 0;
                                foreach (var vId in a.ValueIds)
                                {
                                    valToAttrMap[vId] = a.AttributeId;
                                    await connection.ExecuteAsync(new CommandDefinition(
                                        "INSERT INTO ProductAttributeValueAssignments (ProductId, AttributeId, AttributeValueId, SortOrder, CreatedUtc) VALUES (@ProductId, @AttributeId, @ValueId, @SortOrder, @Now)",
                                        new { ProductId = productId, a.AttributeId, ValueId = vId, SortOrder = valSort++, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                }
                            }

                            if (totalCombinations > 200)
                                throw Error("configuration_limit", "تجاوز الحد الأقصى لعدد التركيبات النشطة (200 تركيبة).");
                        }

                        if (variantMode && (config?.Variants == null || config.Variants.Count == 0))
                            config.Variants = BuildGeneratedVariants(config.Attributes, valToAttrMap, record.Name);

                        if (variantMode && config?.Variants != null && config.Variants.Count > 0)
                        {
                            var defaultVarCmd = config.Variants.FirstOrDefault(v => v.IsDefault);
                            if (defaultVarCmd == null) throw Error("default_variant_required", "يجب تحديد متغير افتراضي واحد.");

                            var combSet = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var v in config.Variants)
                            {
                                var combKey = BuildCombinationKey(v.AttributeValueIds, valToAttrMap);
                                if (!combSet.Add(combKey)) throw Error("duplicate_combination", "توجد تركيبة مطابقة بالفعل.");

                                long vId;
                                var vName = string.IsNullOrWhiteSpace(v.Name) ? record.Name : v.Name.Trim();
                                var normVName = NormalizeFormKc(vName);

                                if (v.IsDefault)
                                {
                                    vId = defaultVariantId;
                                    await connection.ExecuteAsync(new CommandDefinition(
                                        @"UPDATE ProductVariants SET Name=@Name, NormalizedName=@NormName, CombinationKey=@CombKey,
                                        SellPriceOverrideScaled=@SellOverride, PurchasePriceOverrideScaled=@PurchOverride,
                                        IsActive=@IsActive, UpdatedUtc=@Now WHERE Id=@Id",
                                        new { Name = vName, NormName = normVName, CombKey = combKey, SellOverride = v.SellPriceOverrideScaled, PurchOverride = v.PurchasePriceOverrideScaled, v.IsActive, Now = now, Id = vId },
                                        transaction, cancellationToken: ct)).ConfigureAwait(false);
                                }
                                else
                                {
                                    vId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                                        @"INSERT INTO ProductVariants (ProductId, Name, NormalizedName, IsDefault, IsActive, CombinationKey, SellPriceOverrideScaled, PurchasePriceOverrideScaled, ArchivedUtc, Version, CreatedUtc, UpdatedUtc)
                                        VALUES (@ProductId, @Name, @NormName, 0, @IsActive, @CombKey, @SellOverride, @PurchOverride, NULL, 1, @Now, @Now); SELECT last_insert_rowid();",
                                        new { ProductId = productId, Name = vName, NormName = normVName, v.IsActive, CombKey = combKey, SellOverride = v.SellPriceOverrideScaled, PurchOverride = v.PurchasePriceOverrideScaled, Now = now },
                                        transaction, cancellationToken: ct)).ConfigureAwait(false);
                                }

                                foreach (var valId in v.AttributeValueIds)
                                {
                                    if (!valToAttrMap.TryGetValue(valId, out var attrId))
                                        throw Error("validation", "قيمة السمة لا تنتمي لسمات المنتج المحددة.");
                                    await connection.ExecuteAsync(new CommandDefinition(
                                        "INSERT INTO ProductVariantValues (VariantId, ProductId, AttributeId, AttributeValueId) VALUES (@VariantId, @ProductId, @AttributeId, @ValueId)",
                                        new { VariantId = vId, ProductId = productId, AttributeId = attrId, ValueId = valId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                }

                                await ReconcileVariantIdentifiersAsync(connection, transaction, productId, vId, v.Sku, v.Barcodes, unitIdMap, baseProductUnitId, record.UserId, now, ct).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            var simpleVar = config?.Variants?.FirstOrDefault();
                            if (simpleVar != null)
                            {
                                await ReconcileVariantIdentifiersAsync(connection, transaction, productId, defaultVariantId, simpleVar.Sku, simpleVar.Barcodes, unitIdMap, baseProductUnitId, record.UserId, now, ct).ConfigureAwait(false);
                            }
                        }

                        await SaveBundleComponentsAsync(connection,transaction,productId,config?.BundleComponents,ct).ConfigureAwait(false);

                        await AuditAsync(connection, transaction, productId, "product_configuration", productId, "create",
                            SimpleJson(new Dictionary<string, object> { { "id", productId }, { "name", record.Name }, { "variantMode", variantMode }, { "bundleComponents", string.Join(",",(config?.BundleComponents??new BundleComponentConfigRecord[0]).Select(x=>x.VariantId+":"+x.QuantityBaseScaled)) } }), record.UserId, now, ct).ConfigureAwait(false);

                        transaction.Commit();
                        return await FindProductAsync(productId, ct).ConfigureAwait(false);
                    }
                    catch (SQLiteException ex) when (IsUniqueViolation(ex))
                    {
                        TranslateUniqueViolation(ex);
                        throw;
                    }
                }
            }
        }

        public async Task<CatalogProductRecord> UpdateAggregateProductAsync(UpdateProductRecord record, CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var existing = await connection.QuerySingleOrDefaultAsync<ProductStateRow>(
                            new CommandDefinition("SELECT Version, ArchivedUtc FROM ProductDetails WHERE ProductId=@ProductId",
                            new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (existing == null) throw Error("not_found", "المنتج غير موجود.");
                        if (existing.ArchivedUtc != null) throw Error("archived", "المنتج مؤرشف ولا يمكن تعديله.");
                        if (existing.Version != record.Version)
                            throw Error("version_conflict", "عُدلت بيانات المنتج في جلسة أخرى. أعد التحميل ثم حاول مرة أخرى.");

                        await ValidateReferences(connection, transaction, record.CategoryId, record.BaseUnitId, ct).ConfigureAwait(false);

                        var existingDetail = await connection.QuerySingleOrDefaultAsync<CatalogProductRecord>(
                            new CommandDefinition("SELECT BaseUnitId, VariantMode, SellingType FROM ProductDetails WHERE ProductId=@ProductId",
                            new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                        var existingUnits = (await connection.QueryAsync<ProductUnitRecord>(
                        new CommandDefinition(@"SELECT u.Id, u.ProductId, u.UnitId, u.ToBaseNumerator, u.ToBaseDenominator, u.SellPriceScaled, u.UnitPurchasePriceScaled, u.IsBase, u.IsSaleUnit, u.IsPurchaseUnit, u.IsActive, u.FirstUsedUtc, u.Version,
                                CASE WHEN u.FirstUsedUtc IS NOT NULL OR EXISTS (SELECT 1 FROM ProductBarcodes pb WHERE pb.ProductUnitId=u.Id) THEN 1 ELSE 0 END AS IsLocked
                                FROM ProductUnits u WHERE u.ProductId=@ProductId",
                            new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                        var existingVariants = (await connection.QueryAsync<ProductVariantRecord>(
                            new CommandDefinition("SELECT Id, Name, IsDefault, IsActive, CombinationKey, ArchivedUtc, Version, SellPriceOverrideScaled, PurchasePriceOverrideScaled FROM ProductVariants WHERE ProductId=@ProductId",
                            new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                        var existingBarcodes = (await connection.QueryAsync<ProductBarcodeRecord>(
                            new CommandDefinition("SELECT Id, VariantId, ProductUnitId, Barcode, NormalizedBarcode, BarcodeType, IsPrimary, RetiredUtc FROM ProductBarcodes WHERE ProductId=@ProductId",
                            new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                        if (record.BaseUnitId != existingDetail.BaseUnitId)
                            throw Error("base_unit_locked", "لا يمكن تغيير الوحدة الأساسية لمنتج محفوظ؛ أنشئ منتجًا جديدًا بالوحدة المطلوبة.");
                        if (record.SellingType != null && record.SellingType != existingDetail.SellingType)
                            throw Error("selling_type_locked", "لا يمكن تغيير نوع البيع لمنتج محفوظ؛ أنشئ منتجًا جديدًا بالنوع المطلوب.");
                        if (existingDetail.SellingType == "weight" && (existingUnits.Any(u => !u.IsBase && u.IsActive) || record.Configuration?.Units != null && record.Configuration.Units.Any(u => !u.IsBase && u.IsActive)))
                            throw Error("weight_unit_conversion", "البيع بالوزن يستخدم الكمية الأساسية بالكيلوجرام دون تحويلات وحدات إضافية.");

                        var config = record.Configuration;
                        var hasLegacyAxes = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                            "SELECT COUNT(*) FROM ProductAttributeAssignments x JOIN ProductAttributes a ON a.Id=x.AttributeId WHERE x.ProductId=@ProductId AND a.Kind IN ('size','color')",
                            new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false) > 0;
                        var variantMode = hasLegacyAxes ? existingDetail.VariantMode : (config != null ? config.VariantMode : existingDetail.VariantMode);
                        if (hasLegacyAxes && config != null)
                            await ValidateLegacyConfigurationAsync(connection, transaction, record.ProductId, config, existingVariants, ct).ConfigureAwait(false);

                        if (existingVariants.Any(v => !v.IsDefault))
                        {
                            if (!variantMode) throw Error("variant_mode_locked", "لا يمكن إلغاء وضع المتغيرات بعد إنشاء متغيرات للمنتج.");
                        }

                        if (config != null)
                        {
                            ValidateCapabilitiesList(config.CapabilityKeys);
                            ValidateUnitConfiguration(config.Units, record.BaseUnitId);

                            if (config.Units != null)
                            {
                                foreach (var eu in existingUnits)
                                {
                                    var cu = config.Units.FirstOrDefault(u => u.Id == eu.Id);
                                    if (cu == null)
                                    {
                                        if (eu.IsBase || eu.IsLocked) throw Error("unit_locked", "لا يمكن حذف الوحدة الأساسية أو وحدة مرتبطة بباركود أو حركة سابقة.");
                                        continue;
                                    }
                                    if (eu.IsLocked)
                                    {
                                        if (cu.UnitId != eu.UnitId || cu.ToBaseNumerator != eu.ToBaseNumerator || cu.ToBaseDenominator != eu.ToBaseDenominator || cu.IsSaleUnit != eu.IsSaleUnit || cu.IsPurchaseUnit != eu.IsPurchaseUnit)
                                            throw Error("conversion_locked", "لا يمكن تعديل نسبة أو إعدادات وحدة قياس مقفلة.");
                                    }
                                }
                            }

                            if (config.Variants != null)
                            {
                                foreach (var ev in existingVariants)
                                {
                                    var cv = config.Variants.FirstOrDefault(v => v.Id == ev.Id);
                                    if (cv == null) throw Error("configuration_incomplete", "بيانات المتغيرات الحالية غير مكتملة في الطلب.");
                                    if (ev.ArchivedUtc != null && !cv.Archive)
                                        throw Error("variant_archived", "لا يمكن إلغاء أرشفة متغير مؤرشف.");
                                }

                                var unarchivedConfigVariants = config.Variants.Where(v => !v.Archive).ToList();
                                if (unarchivedConfigVariants.Count == 0)
                                    throw Error("validation", "يجب أن يبقى متغير واحد غير مؤرشف على الأقل.");

                                var defaultCount = unarchivedConfigVariants.Count(v => v.IsDefault);
                                if (defaultCount != 1)
                                    throw Error("default_variant_required", "يجب تحديد متغير افتراضي واحد غير مؤرشف.");
                            }
                        }

                        var now = UtcNow();
                        // A receipt mirrors a simple product's price into its sole variant.
                        // Let that mirror inherit an explicitly edited product price. Distinct
                        // overrides and products configured with variants retain their precedence.
                        if (!existingDetail.VariantMode && !variantMode && existingVariants.Count == 1 && existingVariants[0].IsDefault)
                        {
                            await connection.ExecuteAsync(new CommandDefinition(@"
                                UPDATE ProductVariants SET SellPriceOverrideScaled=NULL, Version=Version+1, UpdatedUtc=@Now
                                WHERE Id=@VariantId AND ProductId=@ProductId
                                  AND SellPriceOverrideScaled=(SELECT SellPriceScaled FROM ProductDetails WHERE ProductId=@ProductId)
                                  AND SellPriceOverrideScaled<>@Price
                                  AND EXISTS(SELECT 1 FROM CatalogAuditLog WHERE ProductId=@ProductId
                                    AND EntityType='variant' AND EntityId=@VariantId AND Action='inventory_receipt_price')",
                                new { VariantId=existingVariants[0].Id, record.ProductId, Price=record.SellPriceScaled, Now=now },
                                transaction, cancellationToken: ct)).ConfigureAwait(false);
                        }
                        var affected = await connection.ExecuteAsync(new CommandDefinition(
                            @"UPDATE ProductDetails SET Description=@Description, CategoryId=@CategoryId, BaseUnitId=@BaseUnitId,
                            ProductType=@ProductType, SellPriceScaled=@SellPriceScaled, PurchasePriceScaled=@PurchasePriceScaled, ReorderPointScaled=COALESCE(@ReorderPointScaled,ReorderPointScaled), AllowSaleWhenOutOfStock=@AllowSaleWhenOutOfStock,
                            IsActive=@IsActive, VariantMode=@VariantMode, MigrationState='ready', Version=Version+1, UpdatedUtc=@Now
                            WHERE ProductId=@ProductId AND Version=@Version AND ArchivedUtc IS NULL",
                            new { record.Description, record.CategoryId, record.BaseUnitId, record.ProductType, record.SellPriceScaled, record.PurchasePriceScaled, record.ReorderPointScaled, AllowSaleWhenOutOfStock=record.AllowSaleWhenOutOfStock?1:0, record.IsActive, VariantMode = variantMode ? 1 : 0, Now = now, record.ProductId, record.Version },
                            transaction, cancellationToken: ct)).ConfigureAwait(false);

                        if (affected == 0)
                            throw Error("version_conflict", "عُدلت بيانات المنتج في جلسة أخرى. أعد التحميل ثم حاول مرة أخرى.");

                        await connection.ExecuteAsync(new CommandDefinition(
                            @"UPDATE Products SET Name=@Name WHERE Id=@ProductId;
                            UPDATE ProductMigrationIssues SET ResolvedUtc=@Now WHERE ProductId=@ProductId AND ResolvedUtc IS NULL;",
                            new { record.Name, record.ProductId, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                        if (config != null)
                        {
                            var existingCaps = (await connection.QueryAsync<string>(
                                new CommandDefinition("SELECT CapabilityKey FROM ProductCapabilityAssignments WHERE ProductId=@ProductId",
                                new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                            var activeCaps = new HashSet<string>(config.CapabilityKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                            var priorTracking = (await connection.QueryAsync<string>(new CommandDefinition(
                                "SELECT CapabilityKey FROM ProductCapabilityAssignments WHERE ProductId=@ProductId AND IsEnabled=1 AND CapabilityKey IN ('batch-tracking','expiry-dates','serial-numbers','imei-tracking')",
                                new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToList();
                            if (priorTracking.Any(key => !activeCaps.Contains(key)) &&
                                await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                                    "SELECT COUNT(*) FROM StockMovements m JOIN ProductVariants v ON v.Id=m.VariantId WHERE v.ProductId=@ProductId",
                                    new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false) > 0)
                                throw Error("tracking_history_locked", "لا يمكن إيقاف تتبع منتج لديه حركات مخزون منشورة.");
                            foreach (var key in AllowedCapabilities)
                            {
                                var isEnabled = activeCaps.Contains(key) ? 1 : 0;
                                if (existingCaps.Contains(key, StringComparer.OrdinalIgnoreCase))
                                {
                                    await connection.ExecuteAsync(new CommandDefinition(
                                        "UPDATE ProductCapabilityAssignments SET IsEnabled=@IsEnabled, UpdatedUtc=@Now, ChangedByUserId=@UserId WHERE ProductId=@ProductId AND lower(CapabilityKey)=lower(@Key)",
                                        new { IsEnabled = isEnabled, Now = now, UserId = record.UserId, record.ProductId, Key = key }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                }
                                else if (isEnabled == 1)
                                {
                                    await connection.ExecuteAsync(new CommandDefinition(
                                        "INSERT INTO ProductCapabilityAssignments (ProductId, CapabilityKey, IsEnabled, AssignedUtc, UpdatedUtc, ChangedByUserId) VALUES (@ProductId, @Key, 1, @Now, @Now, @UserId)",
                                        new { record.ProductId, Key = key.ToLowerInvariant(), Now = now, UserId = record.UserId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                }
                            }

                            var unitIdMap = new Dictionary<long, long>();
                            long baseProductUnitId = existingUnits.FirstOrDefault(u => u.IsBase)?.Id ?? 0;
                            if (config.Units != null)
                            {
                                foreach (var u in config.Units)
                                {
                                    if (u.Id.HasValue && u.Id.Value > 0)
                                    {
                                        await connection.ExecuteAsync(new CommandDefinition(
                                            "UPDATE ProductUnits SET IsActive=@IsActive, IsSaleUnit=@IsSaleUnit, IsPurchaseUnit=@IsPurchaseUnit, ToBaseNumerator=@Num, ToBaseDenominator=@Den, SellPriceScaled=@SellPriceScaled, UnitPurchasePriceScaled=@UnitPurchasePriceScaled, Version=Version+1, UpdatedUtc=@Now WHERE Id=@Id AND ProductId=@ProductId",
                                            new { u.IsActive, u.IsSaleUnit, u.IsPurchaseUnit, Num = u.ToBaseNumerator, Den = u.ToBaseDenominator, u.SellPriceScaled, u.UnitPurchasePriceScaled, Now = now, Id = u.Id.Value, record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                        unitIdMap[u.UnitId] = u.Id.Value;
                                        unitIdMap[u.Id.Value] = u.Id.Value;
                                        if (u.IsBase) baseProductUnitId = u.Id.Value;
                                    }
                                    else
                                    {
                                        ValidateRatio(u.ToBaseNumerator, u.ToBaseDenominator);
                                        var newId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                                            @"INSERT INTO ProductUnits (ProductId, UnitId, ToBaseNumerator, ToBaseDenominator, SellPriceScaled, UnitPurchasePriceScaled, IsBase, IsSaleUnit, IsPurchaseUnit, IsActive, FirstUsedUtc, Version, CreatedUtc, UpdatedUtc)
                                            VALUES (@ProductId, @UnitId, @Num, @Den, @SellPriceScaled, @UnitPurchasePriceScaled, 0, @IsSaleUnit, @IsPurchaseUnit, @IsActive, NULL, 1, @Now, @Now); SELECT last_insert_rowid();",
                                            new { record.ProductId, u.UnitId, Num = u.ToBaseNumerator, Den = u.ToBaseDenominator, u.SellPriceScaled, u.UnitPurchasePriceScaled, u.IsSaleUnit, u.IsPurchaseUnit, u.IsActive, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                        unitIdMap[u.UnitId] = newId;
                                    }
                                }
                                foreach (var removed in existingUnits.Where(eu => !eu.IsBase && !eu.IsLocked && !config.Units.Any(cu => cu.Id == eu.Id)))
                                {
                                    await connection.ExecuteAsync(new CommandDefinition(
                                        "UPDATE ProductUnits SET IsActive=0, IsSaleUnit=0, IsPurchaseUnit=0, Version=Version+1, UpdatedUtc=@Now WHERE Id=@Id AND ProductId=@ProductId",
                                        new { Now=now, Id=removed.Id, record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                }
                            }

                        await ValidateAttributeConfigurationAsync(connection, transaction, config, ct).ConfigureAwait(false);
                        var valToAttrMap = new Dictionary<long, long>();
                            if (!hasLegacyAxes && variantMode && config.Attributes != null)
                            {
                                await connection.ExecuteAsync(new CommandDefinition(
                                    "DELETE FROM ProductVariantValues WHERE ProductId=@ProductId; DELETE FROM ProductAttributeValueAssignments WHERE ProductId=@ProductId; DELETE FROM ProductAttributeAssignments WHERE ProductId=@ProductId;",
                                    new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                                foreach (var a in config.Attributes)
                                {
                                    await connection.ExecuteAsync(new CommandDefinition(
                                        "INSERT INTO ProductAttributeAssignments (ProductId, AttributeId, SortOrder, CreatedUtc, UpdatedUtc) VALUES (@ProductId, @AttributeId, @SortOrder, @Now, @Now)",
                                        new { record.ProductId, a.AttributeId, a.SortOrder, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                                    int vSort = 0;
                                    foreach (var vId in a.ValueIds)
                                    {
                                        valToAttrMap[vId] = a.AttributeId;
                                        await connection.ExecuteAsync(new CommandDefinition(
                                            "INSERT INTO ProductAttributeValueAssignments (ProductId, AttributeId, AttributeValueId, SortOrder, CreatedUtc) VALUES (@ProductId, @AttributeId, @ValueId, @SortOrder, @Now)",
                                            new { record.ProductId, a.AttributeId, ValueId = vId, SortOrder = vSort++, Now = now }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                    }
                                }
                            }

                            if (hasLegacyAxes && config.Variants != null)
                            {
                                // Keep all legacy axis rows, combination keys and variant IDs untouched.
                                foreach (var variant in config.Variants)
                                    await ReconcileVariantIdentifiersAsync(connection, transaction, record.ProductId, variant.Id.Value, variant.Sku, variant.Barcodes, unitIdMap, baseProductUnitId, record.UserId, now, ct).ConfigureAwait(false);
                            }
                            else if (variantMode && config.Variants != null)
                            {
                                await connection.ExecuteAsync(new CommandDefinition(
                                    "UPDATE ProductVariants SET IsDefault=0 WHERE ProductId=@ProductId AND IsDefault=1",
                                    new { record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                var combSet = new HashSet<string>(StringComparer.Ordinal);
                                foreach (var v in config.Variants)
                                {
                                    if (v.Archive)
                                    {
                                        if (v.Id.HasValue)
                                        {
                                            await connection.ExecuteAsync(new CommandDefinition(
                                                "UPDATE ProductVariants SET IsActive=0, ArchivedUtc=COALESCE(ArchivedUtc, @Now), Version=Version+1, UpdatedUtc=@Now WHERE Id=@Id AND ProductId=@ProductId",
                                                new { Now = now, Id = v.Id.Value, record.ProductId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                        }
                                        continue;
                                    }

                                    var combKey = BuildCombinationKey(v.AttributeValueIds, valToAttrMap);
                                    if (!combSet.Add(combKey)) throw Error("duplicate_combination", "توجد تركيبة مطابقة بالفعل.");

                                    long vId;
                                    var vName = string.IsNullOrWhiteSpace(v.Name) ? record.Name : v.Name.Trim();
                                    var normVName = NormalizeFormKc(vName);

                                    if (v.Id.HasValue && v.Id.Value > 0)
                                    {
                                        vId = v.Id.Value;
                                        // The application catalog editor omits override mutations.
                                        // Keep exact persisted prices when no new override is supplied.
                                        var persisted = existingVariants.Single(old => old.Id == vId);
                                        v.SellPriceOverrideScaled = v.SellPriceOverrideScaled ?? persisted.SellPriceOverrideScaled;
                                        v.PurchasePriceOverrideScaled = v.PurchasePriceOverrideScaled ?? persisted.PurchasePriceOverrideScaled;
                                        await connection.ExecuteAsync(new CommandDefinition(
                                            @"UPDATE ProductVariants SET Name=@Name, NormalizedName=@NormName, CombinationKey=@CombKey,
                                            IsDefault=@IsDefault, IsActive=@IsActive, SellPriceOverrideScaled=@SellOverride,
                                            PurchasePriceOverrideScaled=@PurchOverride, Version=Version+1, UpdatedUtc=@Now
                                            WHERE Id=@Id AND ProductId=@ProductId",
                                            new { Name = vName, NormName = normVName, CombKey = combKey, v.IsDefault, v.IsActive, SellOverride = v.SellPriceOverrideScaled, PurchOverride = v.PurchasePriceOverrideScaled, Now = now, Id = vId, record.ProductId },
                                            transaction, cancellationToken: ct)).ConfigureAwait(false);
                                    }
                                    else
                                    {
                                        vId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                                            @"INSERT INTO ProductVariants (ProductId, Name, NormalizedName, IsDefault, IsActive, CombinationKey, SellPriceOverrideScaled, PurchasePriceOverrideScaled, ArchivedUtc, Version, CreatedUtc, UpdatedUtc)
                                            VALUES (@ProductId, @Name, @NormName, @IsDefault, @IsActive, @CombKey, @SellOverride, @PurchOverride, NULL, 1, @Now, @Now); SELECT last_insert_rowid();",
                                            new { record.ProductId, Name = vName, NormName = normVName, v.IsDefault, v.IsActive, CombKey = combKey, SellOverride = v.SellPriceOverrideScaled, PurchOverride = v.PurchasePriceOverrideScaled, Now = now },
                                            transaction, cancellationToken: ct)).ConfigureAwait(false);
                                    }

                                    await connection.ExecuteAsync(new CommandDefinition(
                                        "DELETE FROM ProductVariantValues WHERE VariantId=@VariantId",
                                        new { VariantId = vId }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                                    foreach (var valId in v.AttributeValueIds)
                                    {
                                        if (!valToAttrMap.TryGetValue(valId, out var attrId))
                                            throw Error("variant_set_incomplete", "تركيبة المتغير تحتوي على قيم سمات غير محددة للمنتج.");
                                        await connection.ExecuteAsync(new CommandDefinition(
                                            "INSERT INTO ProductVariantValues (VariantId, ProductId, AttributeId, AttributeValueId) VALUES (@VariantId, @ProductId, @AttributeId, @ValueId)",
                                            new { VariantId = vId, ProductId = record.ProductId, AttributeId = attrId, ValueId = valId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                                    }

                                    await ReconcileVariantIdentifiersAsync(connection, transaction, record.ProductId, vId, v.Sku, v.Barcodes, unitIdMap, baseProductUnitId, record.UserId, now, ct).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                var defaultVar = existingVariants.FirstOrDefault(v => v.IsDefault);
                                if (defaultVar != null)
                                {
                                    await connection.ExecuteAsync(new CommandDefinition(
                                        "UPDATE ProductVariants SET IsActive=@IsActive, UpdatedUtc=@Now WHERE Id=@Id",
                                        new { record.IsActive, Now = now, Id = defaultVar.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);

                                    var simpleVarCmd = config?.Variants?.FirstOrDefault();
                                    if (simpleVarCmd != null)
                                    {
                                        await ReconcileVariantIdentifiersAsync(connection, transaction, record.ProductId, defaultVar.Id, simpleVarCmd.Sku, simpleVarCmd.Barcodes, unitIdMap, baseProductUnitId, record.UserId, now, ct).ConfigureAwait(false);
                                    }
                                }
                            }
                        }
                        else
                        {
                            var defaultVar = existingVariants.FirstOrDefault(v => v.IsDefault);
                            if (defaultVar != null)
                            {
                                await connection.ExecuteAsync(new CommandDefinition(
                                    "UPDATE ProductVariants SET IsActive=@IsActive, UpdatedUtc=@Now WHERE Id=@Id",
                                    new { record.IsActive, Now = now, Id = defaultVar.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                            }
                        }

                        if(config!=null)await SaveBundleComponentsAsync(connection,transaction,record.ProductId,config.BundleComponents,ct).ConfigureAwait(false);

                        await AuditAsync(connection, transaction, record.ProductId, "product_configuration", record.ProductId, "update",
                            SimpleJson(new Dictionary<string, object> { { "id", record.ProductId }, { "name", record.Name }, { "bundleComponents", string.Join(",",(config?.BundleComponents??new BundleComponentConfigRecord[0]).Select(x=>x.VariantId+":"+x.QuantityBaseScaled)) } }), record.UserId, now, ct).ConfigureAwait(false);

                        transaction.Commit();
                        return await FindProductAsync(record.ProductId, ct).ConfigureAwait(false);
                    }
                    catch (SQLiteException ex) when (IsUniqueViolation(ex))
                    {
                        TranslateUniqueViolation(ex);
                        throw;
                    }
                }
            }
        }

        private static void ValidateCapabilitiesList(IReadOnlyList<string> capabilityKeys)
        {
            if (capabilityKeys == null) return;
            var selected = new HashSet<string>(capabilityKeys, StringComparer.OrdinalIgnoreCase);
            if (selected.Contains("imei-tracking") && !selected.Contains("serial-numbers"))
                throw Error("capability_dependency", "تتبع IMEI يتطلب تتبع السيريال.");
            foreach (var key in capabilityKeys)
            {
                if (!AllowedCapabilities.Contains(key))
                    throw Error("capability_not_supported", $"الميزة '{key}' غير مدعومة.");
            }
        }

        private static void ValidateUnitConfiguration(IReadOnlyList<ProductUnitConfigRecord> units, long baseUnitId)
        {
            if (units == null || units.Count == 0) return;
            if (units.Count(u => u.IsBase) != 1 || units.First(u => u.IsBase).UnitId != baseUnitId)
                throw Error("validation", "يجب تحديد وحدة أساسية مطابقة لوحدة المنتج الأساسية.");
            var seen = new HashSet<long>();
            foreach (var unit in units)
            {
                if (unit.UnitId <= 0 || !seen.Add(unit.UnitId))
                    throw Error("validation", "لا يمكن تكرار وحدة قياس للمنتج.");
                ValidateRatio(unit.ToBaseNumerator, unit.ToBaseDenominator);
                if (unit.IsBase && (unit.ToBaseNumerator != 1 || unit.ToBaseDenominator != 1))
                    throw Error("validation", "تحويل الوحدة الأساسية يجب أن يكون 1/1.");
                if (!unit.IsSaleUnit && !unit.IsPurchaseUnit)
                    throw Error("validation", "يجب أن تكون الوحدة مخصصة للبيع أو الشراء على الأقل.");
            }
        }

        private static async Task SaveBundleComponentsAsync(SQLiteConnection connection,SQLiteTransaction transaction,long bundleProductId,IReadOnlyList<BundleComponentConfigRecord> components,CancellationToken ct)
        {
            components=components??new BundleComponentConfigRecord[0];
            if(components.Count>50)throw Error("bundle_limit","يمكن إضافة 50 مكونًا كحد أقصى.");
            if(components.Count>0)
            {
                var parent=await connection.QuerySingleAsync<BundleParentState>(new CommandDefinition(@"SELECT VariantMode,SellingType,
                    (SELECT COUNT(*) FROM ProductUnits pu WHERE pu.ProductId=ProductDetails.ProductId AND pu.IsActive=1 AND pu.IsSaleUnit=1) SaleUnitCount,
                    (SELECT COUNT(*) FROM ProductUnits pu WHERE pu.ProductId=ProductDetails.ProductId AND pu.IsActive=1 AND pu.IsSaleUnit=1 AND pu.IsBase=1 AND pu.ToBaseNumerator=1 AND pu.ToBaseDenominator=1) BaseSaleUnitCount
                    FROM ProductDetails WHERE ProductId=@Id",new{Id=bundleProductId},transaction,cancellationToken:ct)).ConfigureAwait(false);
                if(parent.VariantMode||parent.SellingType!="piece"||parent.SaleUnitCount!=1||parent.BaseSaleUnitCount!=1)throw Error("bundle_configuration","المنتج المركب يجب أن يباع بالقطعة بوحدته الأساسية ومن دون متغيرات أو وحدات بيع إضافية.");
            }
            var seen=new HashSet<long>();
            foreach(var component in components)
            {
                if(component==null||component.VariantId<=0||component.QuantityBaseScaled<=0||!seen.Add(component.VariantId))throw Error("bundle_component_invalid","راجع مكونات المنتج وكمياتها الموجبة وغير المكررة.");
                var valid=await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"
                    SELECT COUNT(*) FROM ProductVariants v JOIN ProductDetails d ON d.ProductId=v.ProductId
                    WHERE v.Id=@VariantId AND v.ProductId<>@BundleProductId AND v.IsActive=1 AND v.ArchivedUtc IS NULL
                      AND d.ProductType='stock' AND d.VariantMode=0 AND d.IsActive=1 AND d.ArchivedUtc IS NULL AND d.MigrationState='ready'
                      AND (SELECT COUNT(*) FROM ProductVariants onlyVariant WHERE onlyVariant.ProductId=v.ProductId AND onlyVariant.IsActive=1 AND onlyVariant.ArchivedUtc IS NULL)=1
                      AND NOT EXISTS(SELECT 1 FROM ProductBundleComponents nested WHERE nested.BundleProductId=v.ProductId)
                      AND NOT EXISTS(SELECT 1 FROM ProductCapabilityAssignments a WHERE a.ProductId=v.ProductId AND a.IsEnabled=1 AND a.CapabilityKey IN ('batch-tracking','expiry-dates','serial-numbers','imei-tracking'))",
                    new{component.VariantId,BundleProductId=bundleProductId},transaction,cancellationToken:ct)).ConfigureAwait(false);
                if(valid!=1)throw Error("bundle_component_unavailable","مكونات المنتج المركب يجب أن تكون أصنافًا مخزنية بسيطة ونشطة وغير مركبة ولا تحتاج تتبع تشغيلة أو أرقامًا تسلسلية.");
            }
            if(components.Count>0&&await connection.ExecuteScalarAsync<long>(new CommandDefinition(@"SELECT COUNT(*) FROM ProductCapabilityAssignments
                WHERE ProductId=@Id AND IsEnabled=1 AND CapabilityKey IN ('batch-tracking','expiry-dates','serial-numbers','imei-tracking')",new{Id=bundleProductId},transaction,cancellationToken:ct)).ConfigureAwait(false)>0)
                throw Error("bundle_parent_tracking","لا يمكن تفعيل تتبع التشغيلة أو الأرقام التسلسلية على المنتج المركب.");
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM ProductBundleComponents WHERE BundleProductId=@BundleProductId",new{BundleProductId=bundleProductId},transaction,cancellationToken:ct)).ConfigureAwait(false);
            foreach(var component in components)await connection.ExecuteAsync(new CommandDefinition("INSERT INTO ProductBundleComponents(BundleProductId,ComponentVariantId,QuantityBaseScaled) VALUES(@BundleProductId,@VariantId,@Quantity)",new{BundleProductId=bundleProductId,VariantId=component.VariantId,Quantity=component.QuantityBaseScaled},transaction,cancellationToken:ct)).ConfigureAwait(false);
        }
        private sealed class BundleParentState{public bool VariantMode{get;set;}public string SellingType{get;set;}public long SaleUnitCount{get;set;}public long BaseSaleUnitCount{get;set;}}

        private static async Task ValidateLegacyConfigurationAsync(SQLiteConnection connection, SQLiteTransaction transaction, long productId, ProductConfigurationRecord config, IReadOnlyList<ProductVariantRecord> existing, CancellationToken ct)
        {
            // Retired axes are opaque: callers may edit product metadata/identifiers, never rebuild legacy combinations.
            var custom = (await connection.QueryAsync<LegacyValue>(new CommandDefinition(
                @"SELECT x.AttributeId, x.AttributeValueId ValueId FROM ProductAttributeValueAssignments x
                  JOIN ProductAttributes a ON a.Id=x.AttributeId WHERE x.ProductId=@ProductId AND a.Kind='custom'",
                new { ProductId=productId }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToList();
            if (config.Attributes != null && !custom.Select(x=>x.AttributeId+":"+x.ValueId).OrderBy(x=>x).SequenceEqual(
                config.Attributes.SelectMany(a=>(a.ValueIds??new long[0]).Select(v=>a.AttributeId+":"+v)).OrderBy(x=>x)))
                throw Error("legacy_configuration_locked", "لا يمكن تغيير تركيبات المنتج القديمة؛ يمكن تعديل بيانات المنتج ومعرفاته.");
            if (config.Variants == null) return;
            if (config.Variants.Count != existing.Count || config.Variants.Select(v=>v.Id).Distinct().Count()!=existing.Count)
                throw Error("legacy_configuration_locked", "يجب الاحتفاظ بالمتغيرات المحفوظة.");
            foreach (var variant in config.Variants)
            {
                var prior = existing.SingleOrDefault(v=>v.Id==variant.Id);
                if (prior == null || prior.IsDefault != variant.IsDefault || prior.IsActive != variant.IsActive || (prior.ArchivedUtc!=null)!=variant.Archive)
                    throw Error("legacy_configuration_locked", "يجب الاحتفاظ بالمتغيرات المحفوظة.");
                var values = await connection.QueryAsync<long>(new CommandDefinition(
                    @"SELECT v.AttributeValueId FROM ProductVariantValues v JOIN ProductAttributes a ON a.Id=v.AttributeId
                      WHERE v.VariantId=@Id AND a.Kind='custom'", new { Id=prior.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                if (!values.OrderBy(v=>v).SequenceEqual((variant.AttributeValueIds??new long[0]).OrderBy(v=>v)))
                    throw Error("legacy_configuration_locked", "لا يمكن تغيير تركيبات المنتج القديمة.");
            }
        }

        private sealed class LegacyValue { public long AttributeId { get; set; } public long ValueId { get; set; } }

        private static async Task ValidateAttributeConfigurationAsync(SQLiteConnection connection, SQLiteTransaction transaction, ProductConfigurationRecord config, CancellationToken ct)
        {
            foreach (var assignment in config?.Attributes ?? new ProductAttributeAssignmentConfigRecord[0])
            {
                var available = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COUNT(*) FROM ProductAttributes WHERE Id=@AttributeId AND Kind='custom'",
                    new { assignment.AttributeId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                if (available != 1) throw Error("attribute_not_available", "السمة غير متاحة.");
                foreach (var valueId in assignment.ValueIds ?? new long[0])
                    if (await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                        "SELECT COUNT(*) FROM ProductAttributeValues WHERE Id=@ValueId AND AttributeId=@AttributeId",
                        new { ValueId=valueId, assignment.AttributeId }, transaction, cancellationToken: ct)).ConfigureAwait(false) != 1)
                        throw Error("attribute_not_available", "قيمة السمة غير متاحة.");
            }
        }

        private static IReadOnlyList<ProductVariantConfigRecord> BuildGeneratedVariants(IReadOnlyList<ProductAttributeAssignmentConfigRecord> attributes, Dictionary<long, long> valueToAttribute, string productName)
        {
            var groups = (attributes ?? new ProductAttributeAssignmentConfigRecord[0]).Where(a => a.ValueIds != null && a.ValueIds.Count > 0).ToList();
            if (groups.Count == 0) return new List<ProductVariantConfigRecord>();
            var combinations = new List<List<long>> { new List<long>() };
            foreach (var group in groups)
            {
                var next = new List<List<long>>();
                foreach (var prior in combinations)
                    foreach (var valueId in group.ValueIds)
                    {
                        var item = new List<long>(prior); item.Add(valueId); next.Add(item);
                    }
                combinations = next;
            }
            if (combinations.Count > 200) throw Error("configuration_limit", "تجاوز الحد الأقصى لعدد التركيبات النشطة (200 تركيبة).");
            return combinations.Select((values, index) => new ProductVariantConfigRecord { Name = productName, AttributeValueIds = values, IsDefault = index == 0, IsActive = true, Barcodes = new List<ProductBarcodeConfigRecord>() }).ToList();
        }

        private static string BuildCombinationKey(IReadOnlyList<long> valueIds, Dictionary<long, long> valToAttrMap)
        {
            if (valueIds == null || valueIds.Count == 0) return null;
            var pairs = new List<(long AttrId, long ValId)>();
            foreach (var vId in valueIds)
            {
                if (!valToAttrMap.TryGetValue(vId, out var aId))
                    throw Error("variant_set_incomplete", "قيمة السمة غير معينة للمنتج.");
                pairs.Add((aId, vId));
            }
            pairs.Sort((x, y) => x.AttrId.CompareTo(y.AttrId));
            return string.Join("|", pairs.Select(p => p.AttrId + ":" + p.ValId));
        }

        private static async Task ReconcileVariantIdentifiersAsync(
            SQLiteConnection connection, SQLiteTransaction transaction,
            long productId, long variantId,
            string sku, IReadOnlyList<ProductBarcodeConfigRecord> barcodes,
            Dictionary<long, long> unitIdMap, long defaultProductUnitId,
            long userId, string now, CancellationToken ct)
        {
            var activeSku = await connection.QuerySingleOrDefaultAsync<ProductSkuRecord>(
                new CommandDefinition("SELECT Id, Sku, NormalizedSku FROM ProductSkus WHERE VariantId=@VariantId AND RetiredUtc IS NULL",
                new { VariantId = variantId }, transaction, cancellationToken: ct)).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(sku))
            {
                var normSku = ValidateIdentifier(sku, "SKU");
                var upperNormSku = NormalizeFormKc(normSku);

                if (activeSku == null || !string.Equals(activeSku.NormalizedSku, upperNormSku, StringComparison.Ordinal))
                {
                    if (activeSku != null)
                    {
                        await connection.ExecuteAsync(new CommandDefinition(
                            "UPDATE ProductSkus SET RetiredUtc=@Now, RetiredByUserId=@UserId WHERE Id=@Id",
                            new { Now = now, UserId = userId, Id = activeSku.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    }

                    var dup = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                        "SELECT COUNT(*) FROM ProductSkus WHERE NormalizedSku=@Norm",
                        new { Norm = upperNormSku }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    if (dup > 0) throw Error("duplicate_sku", "رمز SKU مستخدم بالفعل أو تم استخدامه سابقًا.");

                    await connection.ExecuteAsync(new CommandDefinition(
                        @"INSERT INTO ProductSkus (ProductId, VariantId, Sku, NormalizedSku, RetiredUtc, RetiredByUserId, CreatedUtc, CreatedByUserId)
                        VALUES (@ProductId, @VariantId, @Sku, @Norm, NULL, NULL, @Now, @UserId)",
                        new { ProductId = productId, VariantId = variantId, Sku = normSku, Norm = upperNormSku, Now = now, UserId = userId },
                        transaction, cancellationToken: ct)).ConfigureAwait(false);
                }
            }
            else if (activeSku != null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE ProductSkus SET RetiredUtc=@Now, RetiredByUserId=@UserId WHERE Id=@Id",
                    new { Now = now, UserId = userId, Id = activeSku.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false);
            }

            if (barcodes != null)
            {
                foreach (var b in barcodes)
                {
                    if (b.Retire)
                    {
                        if (b.Id.HasValue)
                        {
                            await connection.ExecuteAsync(new CommandDefinition(
                                "UPDATE ProductBarcodes SET RetiredUtc=@Now, RetiredByUserId=@UserId, IsPrimary=0 WHERE Id=@Id AND ProductId=@ProductId",
                                new { Now = now, UserId = userId, Id = b.Id.Value, ProductId = productId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        }
                        continue;
                    }

                    if (b.Id.HasValue && b.Id.Value > 0)
                    {
                        // Existing active barcode: can only update primary state
                        await connection.ExecuteAsync(new CommandDefinition(
                            "UPDATE ProductBarcodes SET IsPrimary=@IsPrimary WHERE Id=@Id AND ProductId=@ProductId AND RetiredUtc IS NULL",
                            new { b.IsPrimary, Id = b.Id.Value, ProductId = productId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    }
                    else if (!string.IsNullOrWhiteSpace(b.Barcode))
                    {
                        var normBarcode = ValidateIdentifier(b.Barcode, "الباركود");
                        try { BarcodeRules.Validate(normBarcode, "code128"); }
                        catch (ArgumentException ex) { throw Error("identifier_invalid", ex.Message); }
                        var upperNormBarcode = NormalizeFormKc(normBarcode);
                        var type = (b.BarcodeType ?? "gtin").ToLowerInvariant();
                        if (type != "gtin" && type != "internal" && type != "supplier")
                            throw Error("validation", "نوع الباركود غير صالح.");

                        long puId = defaultProductUnitId;
                        if (b.ProductUnitId.HasValue && unitIdMap.TryGetValue(b.ProductUnitId.Value, out var mappedId))
                            puId = mappedId;
                        else if (b.ProductUnitId.HasValue && b.ProductUnitId.Value > 0)
                            puId = b.ProductUnitId.Value;

                        var dup = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                            "SELECT COUNT(*) FROM ProductBarcodes WHERE NormalizedBarcode=@Norm",
                            new { Norm = upperNormBarcode }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                        if (dup > 0) throw Error("duplicate_barcode", "الباركود مستخدم بالفعل أو تم استخدامه سابقًا.");

                        await connection.ExecuteAsync(new CommandDefinition(
                            @"INSERT INTO ProductBarcodes (ProductId, VariantId, ProductUnitId, Barcode, NormalizedBarcode, BarcodeType, IsPrimary, RetiredUtc, RetiredByUserId, CreatedUtc, CreatedByUserId)
                            VALUES (@ProductId, @VariantId, @ProductUnitId, @Barcode, @Norm, @Type, @IsPrimary, NULL, NULL, @Now, @UserId)",
                            new { ProductId = productId, VariantId = variantId, ProductUnitId = puId, Barcode = normBarcode, Norm = upperNormBarcode, Type = type, b.IsPrimary, Now = now, UserId = userId },
                            transaction, cancellationToken: ct)).ConfigureAwait(false);
                    }
                }

                // Deterministically repair primary barcode per (VariantId, ProductUnitId)
                var pairs = (await connection.QueryAsync<(long VId, long PUId)>(new CommandDefinition(
                    "SELECT DISTINCT VariantId, ProductUnitId FROM ProductBarcodes WHERE VariantId=@VariantId AND RetiredUtc IS NULL",
                    new { VariantId = variantId }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                foreach (var pair in pairs)
                {
                    var activeCodes = (await connection.QueryAsync<long>(new CommandDefinition(
                        "SELECT Id FROM ProductBarcodes WHERE VariantId=@VId AND ProductUnitId=@PUId AND RetiredUtc IS NULL ORDER BY IsPrimary DESC, Id ASC",
                        new { pair.VId, pair.PUId }, transaction, cancellationToken: ct)).ConfigureAwait(false)).ToList();

                    if (activeCodes.Count > 0)
                    {
                        var primaryId = activeCodes[0];
                        await connection.ExecuteAsync(new CommandDefinition(
                            "UPDATE ProductBarcodes SET IsPrimary = CASE WHEN Id=@PrimaryId THEN 1 ELSE 0 END WHERE VariantId=@VId AND ProductUnitId=@PUId AND RetiredUtc IS NULL",
                            new { PrimaryId = primaryId, pair.VId, pair.PUId }, transaction, cancellationToken: ct)).ConfigureAwait(false);
                    }
                }
            }
        }

        private static void TranslateUniqueViolation(SQLiteException ex)
        {
            var msg = ex.Message ?? "";
            if (msg.IndexOf("UX_ProductAttributes_Key", StringComparison.OrdinalIgnoreCase) >= 0)
                throw Error("duplicate_attribute", "كود السمة مستخدم بالفعل.");
            if (msg.IndexOf("UX_ProductAttributeValues_Name", StringComparison.OrdinalIgnoreCase) >= 0)
                throw Error("duplicate_attribute_value", "قيمة السمة مستخدمة بالفعل.");
            if (msg.IndexOf("UX_ProductVariants_Combination", StringComparison.OrdinalIgnoreCase) >= 0)
                throw Error("duplicate_combination", "توجد تركيبة مطابقة بالفعل.");
            if (msg.IndexOf("UX_ProductSkus_Normalized", StringComparison.OrdinalIgnoreCase) >= 0)
                throw Error("duplicate_sku", "رمز SKU مستخدم بالفعل أو تم استخدامه سابقًا.");
            if (msg.IndexOf("UX_ProductBarcodes_Normalized", StringComparison.OrdinalIgnoreCase) >= 0)
                throw Error("duplicate_barcode", "الباركود مستخدم بالفعل أو تم استخدامه سابقًا.");
            if (msg.IndexOf("UX_ProductUnits_ActiveUnit", StringComparison.OrdinalIgnoreCase) >= 0)
                throw Error("invalid_conversion", "توجد وحدة مطابقة نشطة بالفعل لهذا المنتج.");
            if (msg.IndexOf("UX_ProductUnits_Base", StringComparison.OrdinalIgnoreCase) >= 0)
                throw Error("invalid_conversion", "لا يمكن وجود أكثر من وحدة قياس أساسية واحدة للمنتج.");
            if (msg.IndexOf("catalog_audit_immutable", StringComparison.OrdinalIgnoreCase) >= 0)
                throw Error("catalog_audit_immutable", "سجل تدقيق الكتالوج غير قابل للتعديل أو الحذف.");
        }

        private async Task AuditAsync(SQLiteConnection connection, SQLiteTransaction transaction, long? productId, string entityType, long? entityId, string action, string changeJson, long userId, string now, CancellationToken ct)
        {
            if (userId <= 0)
            {
                var firstUser = await connection.QuerySingleOrDefaultAsync<long?>(
                    new CommandDefinition("SELECT Id FROM Users ORDER BY Id LIMIT 1", transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);
                if (firstUser.HasValue) userId = firstUser.Value;
                else return; // If no users exist, skip audit to avoid FK violation
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO CatalogAuditLog (ProductId, EntityType, EntityId, Action, ChangeJson, ChangedByUserId, ChangedUtc) VALUES (@ProductId, @EntityType, @EntityId, @Action, @ChangeJson, @UserId, @Now)",
                new { ProductId = productId, EntityType = entityType, EntityId = entityId, Action = action, ChangeJson = changeJson, UserId = userId, Now = now },
                transaction, cancellationToken: ct)).ConfigureAwait(false);
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw Error("validation", "كود السمة مطلوب.");
            return value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        }

        private static string NormalizeFormKc(string value)
        {
            return (value ?? "").Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
        }

        private static string NormalizeText(string value, int min, int max, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) throw Error("validation", label + " مطلوب.");
            var trimmed = value.Trim().Normalize(NormalizationForm.FormKC);
            if (trimmed.Length < min || trimmed.Length > max)
                throw Error("validation", $"{label} يجب أن يكون بين {min} و {max} حرف.");
            return trimmed;
        }

        private static string ValidateIdentifier(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) throw Error("validation", label + " مطلوب.");
            var norm = value.Normalize(NormalizationForm.FormKC).Trim();
            if (norm.Length < 1 || norm.Length > 64)
                throw Error("identifier_invalid", $"{label} يجب أن يكون بين 1 و 64 حرف.");
            if (norm.Any(char.IsWhiteSpace) || norm.Any(char.IsControl))
                throw Error("identifier_invalid", $"{label} لا يجوز أن يحتوي على مسافات أو رموز تحكم.");
            return norm;
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                long temp = b;
                b = a % b;
                a = temp;
            }
            return Math.Abs(a);
        }

        private static void ValidateRatio(long num, long den)
        {
            if (num <= 0 || num > 1000000000 || den <= 0 || den > 1000000000 || Gcd(num, den) != 1)
                throw Error("invalid_conversion", "نسبة التحويل غير صالحة أو غير مبسطة لأبسط صورة.");
        }

        private static bool IsUniqueViolation(SQLiteException ex)
        {
            return ex.ResultCode == SQLiteErrorCode.Constraint || ex.Message.IndexOf("UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SimpleJson(Dictionary<string, object> dict)
        {
            var pairs = dict.Select(kv => $"\"{kv.Key}\":\"{kv.Value?.ToString()?.Replace("\"", "\\\"")}\"");
            return "{" + string.Join(",", pairs) + "}";
        }
    }
}
