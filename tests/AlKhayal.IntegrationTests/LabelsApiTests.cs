using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Printing;
using AlKhayal.Application.Users;
using AlKhayal.Data.Authentication;
using AlKhayal.Server.Host;
using Dapper;
using Newtonsoft.Json.Linq;
using Xunit;
using ZXing;

namespace AlKhayal.IntegrationTests
{
    public sealed partial class TreasuryApiTests
    {
        [Fact]
        public async Task Labels_HTTP_settings_permissions_module_CSFR_origin_barcode_persistence_POS_and_batch_replay()
        {
            using (var f = new TreasuryPersistenceTests.Fixture())
            {
                var product = await LabelPersistenceTests.Product(f); var frontend = Path.Combine(Path.GetDirectoryName(f.DatabasePath), "frontend"); Directory.CreateDirectory(Path.Combine(frontend, "data"));
                File.WriteAllText(Path.Combine(frontend, "data", "screens.json"), "{\"screens\":[{\"route\":\"products-list\",\"title\":\"المنتجات\",\"module\":\"المخازن\"},{\"route\":\"settings-printers\",\"title\":\"الطباعة\",\"module\":\"الإعدادات\",\"administratorOnly\":true}]}");
                var password = Guid.NewGuid().ToString("N"); var store = new SqliteUserStore(f.ConnectionString); var hasher = new PasswordHasher();
                await store.CreateAsync("label-admin", "مدير", hasher.Hash(password), UserRoles.Administrator, CancellationToken.None);
                var cashierId = await store.CreateAsync("label-cashier", "كاشير", hasher.Hash(password), UserRoles.Cashier, CancellationToken.None);
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); var backend = new LabelPersistenceTests.Backend();
                using (var server = new AlKhayalServer(new TestLicenseGate()))
                {
                    var url = new Uri("http://127.0.0.1:" + port); server.Start(new HostOptions { DatabasePath = f.DatabasePath, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') }, backend);
                    using (var admin = Client(url)) using (var cashier = Client(url)) using (var anon = Client(url))
                    {
                        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/labels/settings")).StatusCode);
                        var csrf = await Login(admin, "label-admin", password); var cashCsrf = await Login(cashier, "label-cashier", password);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/labels/settings")).StatusCode);
                        var settings = LabelService.Defaults(); settings.Profiles[0].PrinterName = "Labels";
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Put, "api/labels/settings", settings, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/labels/settings", settings, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/labels/preview", settings.Templates[0], csrf)).StatusCode); Assert.Equal(0, backend.Labels);
                        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("api/labels/options")).StatusCode);
                        var barcode = new { productId = product.Id, variantId = product.Variants[0].Id, productUnitId = product.Units[0].Id, version = product.Version, generate = true, format = "code128", operationKey = "http-barcode" };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, HttpMethod.Post, "api/labels/barcodes", barcode, null)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/labels/barcodes", barcode, cashCsrf)).StatusCode);
                        var saved = await Send(admin, HttpMethod.Post, "api/labels/barcodes", barcode, csrf); Assert.Equal(HttpStatusCode.OK, saved.StatusCode); var data = JObject.Parse(await saved.Content.ReadAsStringAsync())["data"]; var id = (long)data["id"]; var code = (string)data["code"];
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/labels/barcodes", barcode, csrf)).StatusCode);
                        var lookup = JObject.Parse(await admin.GetStringAsync("api/catalog/barcodes/lookup?code=" + code))["data"]; Assert.Equal(product.Id, (long)lookup["productId"]); Assert.Equal(product.Variants[0].Id, (long)lookup["variantId"]);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/features/modules/barcode-label-printing", new { isEnabled = true }, csrf)).StatusCode);
                        var preview = await Send(admin, HttpMethod.Post, "api/labels/product-preview", new { barcodeId = id, format = "code128" }, csrf); Assert.Equal(HttpStatusCode.OK, preview.StatusCode); Assert.Equal(0, backend.Labels);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/labels/options")).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/users/" + cashierId + "/screen-permissions", new { screenIds = new[] { "products-list" } }, csrf)).StatusCode);
                        var options = JObject.Parse(await cashier.GetStringAsync("api/labels/options"))["data"];
                        var configuredProfile = options["profiles"].Single(x => (string)x["id"] == (string)options["defaultProfileId"]);
                        var previewData = JObject.Parse(await preview.Content.ReadAsStringAsync())["data"];
                        Assert.Equal(code, (string)previewData["barcode"]);
                        Assert.Equal((string)configuredProfile["id"], (string)previewData["profileId"]);
                        Assert.Equal((string)configuredProfile["templateId"], (string)previewData["templateId"]);
                        Assert.Equal((decimal)configuredProfile["paper"]["pageWidthMm"], (decimal)previewData["pageWidthMm"]);
                        Assert.Equal((decimal)configuredProfile["paper"]["pageHeightMm"], (decimal)previewData["pageHeightMm"]);
                        Assert.True((bool)previewData["printerConfigured"]);
                        var request = new { operationKey = "http-label-batch", profileId = (string)previewData["profileId"], templateId = (string)previewData["templateId"], exactQuantity = true, items = new[] { new { barcodeId = id, quantity = 3, format = "code128" } } };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Post, "api/labels/jobs", request, null)).StatusCode);
                        using (var cross = new HttpRequestMessage(HttpMethod.Post, "api/labels/jobs")) { cross.Headers.Add("Origin", "https://untrusted.invalid"); cross.Headers.Add("X-CSRF-Token", cashCsrf); cross.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"); Assert.Equal(HttpStatusCode.Forbidden, (await cashier.SendAsync(cross)).StatusCode); }
                        var printed = await Send(cashier, HttpMethod.Post, "api/labels/jobs", request, cashCsrf); Assert.Equal(HttpStatusCode.OK, printed.StatusCode); Assert.Equal("submitted_unknown", (string)JObject.Parse(await printed.Content.ReadAsStringAsync())["data"]["status"]);
                        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, HttpMethod.Post, "api/labels/jobs", request, cashCsrf)).StatusCode); Assert.Equal(1, backend.Labels); Assert.Equal(3, backend.LastBatch.Items[0].Quantity);
                        var printedLabel = Assert.Single(backend.LastBatch.Items);
                        Assert.Equal((string)previewData["barcode"], printedLabel.Barcode);
                        Assert.Equal((string)previewData["productName"], printedLabel.Fields["productName"]);
                        Assert.Equal((string)previewData["price"], printedLabel.Fields["price"]);
                        Assert.Equal((string)previewData["unit"], printedLabel.Fields["unit"]);
                        Assert.Equal("The Cashier", printedLabel.Fields["company"]);
                        Assert.Equal("code128", printedLabel.Format);
                        Assert.Equal((string)previewData["profileId"], backend.Profile.Id);
                        Assert.Equal((string)previewData["printerName"], backend.Profile.PrinterName);
                        Assert.Equal((string)previewData["templateId"], backend.Template.Id);
                        Assert.Equal((decimal)previewData["pageWidthMm"], backend.Template.Stock.PageWidthMm);
                        Assert.Equal((decimal)previewData["pageHeightMm"], backend.Template.Stock.PageHeightMm);
                        Assert.Equal(1, backend.Profile.Copies);
                        Assert.Equal(HttpStatusCode.Conflict, (await Send(cashier, HttpMethod.Post, "api/labels/jobs", new { operationKey = "http-label-batch", items = new[] { new { barcodeId = id, quantity = 4, format = "code128" } } }, cashCsrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/printing/jobs")).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "api/labels/jobs", new { operationKey = "bad-quantity", items = new[] { new { barcodeId = id, quantity = 0, format = "code128" } } }, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, "api/labels/test/labels-default", new { operationKey = "http-label-test" }, csrf)).StatusCode); Assert.Equal(2, backend.Labels);
                        var second = await LabelPersistenceTests.Product(f, "English second product", salePrice: "23.75");
                        var ean = await LabelPersistenceTests.Assign(f, second, "400638133393", format: "ean13");
                        backend.RenderLabels = true;
                        var design = new LabelDesignRequest { Version = 1, Elements = new List<LabelDesignElement> {
                            new LabelDesignElement { Key = "company", Bold = true },
                            new LabelDesignElement { Key = "productName", Alignment = "left" },
                            new LabelDesignElement { Key = "barcode" },
                            new LabelDesignElement { Key = "price", FontSize = 9, Alignment = "right" },
                            new LabelDesignElement { Key = "barcodeText", Visible = false },
                            new LabelDesignElement { Key = "static", Text = "شكرًا لزيارتكم" }
                        } };
                        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, HttpMethod.Put, "api/labels/design", design, cashCsrf)).StatusCode);
                        var designPreview = await Send(admin, HttpMethod.Post, "api/labels/design/preview", design, csrf);
                        Assert.Equal(HttpStatusCode.OK, designPreview.StatusCode);
                        Assert.Equal("The Cashier", backend.PreviewBusinessName);
                        var samplePng = (string)JObject.Parse(await designPreview.Content.ReadAsStringAsync())["data"]["pngBase64"];
                        using (var sampleStream = new MemoryStream(Convert.FromBase64String(samplePng)))
                        using (var sampleImage = new Bitmap(sampleStream))
                            Assert.Equal(new Size(591, 355), sampleImage.Size);
                        foreach (var stock in new[] { new Size(35, 25), new Size(40, 30), new Size(60, 40) })
                        {
                            var draft = new LabelDesignPreviewRequest { Version = design.Version, Elements = design.Elements,
                                StockOverride = new LabelPrinterAssignment { PrinterName = "Labels", WidthMm = stock.Width, HeightMm = stock.Height } };
                            var sized = await Send(admin, HttpMethod.Post, "api/labels/design/preview", draft, csrf);
                            Assert.Equal(HttpStatusCode.OK, sized.StatusCode);
                            var png = (string)JObject.Parse(await sized.Content.ReadAsStringAsync())["data"]["pngBase64"];
                            using (var stream = new MemoryStream(Convert.FromBase64String(png)))
                            using (var image = new Bitmap(stream))
                                Assert.Equal(new Size((int)Math.Ceiling(stock.Width / 25.4 * 300), (int)Math.Ceiling(stock.Height / 25.4 * 300)), image.Size);
                        }
                        var crowded = new LabelDesignPreviewRequest { Version = design.Version, Elements = design.Elements,
                            StockOverride = new LabelPrinterAssignment { WidthMm = 20, HeightMm = 16 } };
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "api/labels/design/preview", crowded, csrf)).StatusCode);
                        Assert.Equal(2, backend.Labels);
                        var pngPath = Environment.GetEnvironmentVariable("THECASHIER_LABEL_DESIGN_PNG");
                        if (!string.IsNullOrEmpty(pngPath)) File.WriteAllBytes(pngPath, Convert.FromBase64String(samplePng));
                        const string businessName = "متجر الاختبار";
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/settings/company", new { businessName }, csrf)).StatusCode);
                        var updatedPreview = await Send(admin, HttpMethod.Post, "api/labels/design/preview", design, csrf);
                        Assert.Equal(HttpStatusCode.OK, updatedPreview.StatusCode);
                        Assert.Equal(businessName, backend.PreviewBusinessName);
                        Assert.NotEqual(samplePng, (string)JObject.Parse(await updatedPreview.Content.ReadAsStringAsync())["data"]["pngBase64"]);
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/labels/design", design, csrf)).StatusCode);
                        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, HttpMethod.Put, "api/labels/design", design, csrf)).StatusCode);
                        var reopened = LabelPersistenceTests.Service(f, backend, out _);
                        Assert.Equal("شكرًا لزيارتكم", (await reopened.SettingsAsync(CancellationToken.None)).Design.Last().Text);
                        foreach (var labelCase in new[] { new { Id = id, Code = code, Format = "code128", ProductId = product.Id, Price = "12.50" },
                            new { Id = ean.Id, Code = ean.Code, ean.Format, ProductId = second.Id, Price = "23.75" } })
                        {
                            var actual = await Send(cashier, HttpMethod.Post, "api/labels/product-preview", new { barcodeId = labelCase.Id, format = labelCase.Format }, cashCsrf);
                            Assert.Equal(HttpStatusCode.OK, actual.StatusCode);
                            var actualData = JObject.Parse(await actual.Content.ReadAsStringAsync())["data"];
                            var job = await Send(cashier, HttpMethod.Post, "api/labels/jobs", new { operationKey = "custom-label-" + labelCase.Id,
                                profileId = (string)actualData["profileId"], templateId = labelCase.Id == id ? (string)actualData["templateId"] : null,
                                exactQuantity = true, items = new[] { new { barcodeId = labelCase.Id, quantity = 2, format = labelCase.Format } } }, cashCsrf);
                            Assert.Equal(HttpStatusCode.OK, job.StatusCode);
                            Assert.Equal("submitted_unknown", (string)JObject.Parse(await job.Content.ReadAsStringAsync())["data"]["status"]);
                            using (var previewStream = new MemoryStream(Convert.FromBase64String((string)actualData["pngBase64"])))
                            using (var captureStream = new MemoryStream(backend.CapturedLabel))
                            using (var previewImage = new Bitmap(previewStream))
                            using (var printedImage = new Bitmap(captureStream))
                            {
                                Assert.Equal(new Size(591, 355), printedImage.Size);
                                Assert.Equal(previewImage.Size, printedImage.Size);
                                var decoder = new BarcodeReader { Options = { TryHarder = true } };
                                Assert.Equal(labelCase.Code, decoder.Decode(previewImage)?.Text);
                                Assert.Equal(labelCase.Code, decoder.Decode(printedImage)?.Text);
                                var differentPixels = 0;
                                for (int y = 0; y < printedImage.Height; y++)
                                    for (int x = 0; x < printedImage.Width; x++)
                                        if (previewImage.GetPixel(x, y) != printedImage.GetPixel(x, y)) differentPixels++;
                                Assert.InRange(differentPixels, 0, 25);
                            }
                            var printedProduct = Assert.Single(backend.LastBatch.Items);
                            Assert.Equal(labelCase.ProductId, printedProduct.ProductId);
                            Assert.Equal(labelCase.Code, printedProduct.Barcode);
                            Assert.Equal(labelCase.Format, printedProduct.Format);
                            Assert.Contains(labelCase.Price, printedProduct.Fields["price"]);
                            Assert.Equal(businessName, printedProduct.Fields["company"]);
                            Assert.Equal(2, printedProduct.Quantity);
                            Assert.Equal("Labels", backend.Profile.PrinterName);
                            Assert.Equal(new[] { "company", "productName", "barcode", "price", "static" }, backend.Template.Elements.Where(e => e.Visible).Select(e => e.Key));
                        }
                        design.Version = 2;
                        design.Elements.Single(e => e.Key == "barcode").Visible = false;
                        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Put, "api/labels/design", design, csrf)).StatusCode);
                        design.Elements = null;
                        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Put, "api/labels/design", design, csrf)).StatusCode);
                        var reset = await reopened.SettingsAsync(CancellationToken.None);
                        Assert.Null(reset.Design);
                        Assert.Equal("Labels", reset.Profiles[0].PrinterName);
                        Assert.Equal(50m, reset.Templates[0].Stock.PageWidthMm);
                        using (var connection = f.Open())
                            connection.Execute("UPDATE LabelConfiguration SET Json=json_set(Json,'$.Design',@invalid) WHERE Id=1",
                                new { invalid = "invalid stored layout" });
                        var fallback = await reopened.PreviewProductAsync(new LabelPreviewRequest { BarcodeId = id, Format = "code128" }, CancellationToken.None);
                        Assert.Equal(code, fallback.Barcode);
                        var fallbackJob = await reopened.SubmitAsync(new LabelBatchRequest { OperationKey = "fallback-label",
                            Items = new List<LabelSelection> { new LabelSelection { BarcodeId = id } } }, f.UserId, CancellationToken.None);
                        Assert.Equal("submitted_unknown", fallbackJob.Status);
                        Assert.False(backend.Template.UseConfiguredTextAlignment);
                        Assert.Equal(new[] { "productName", "barcode", "barcodeText", "price" }, backend.Template.Elements.Where(e => e.Visible).Select(e => e.Key));
                        var deleted = await Send(admin, HttpMethod.Post, "api/labels/barcodes/delete", new { productId = product.Id, barcodeId = id, version = (long)data["version"], operationKey = "http-barcode-delete" }, csrf); Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
                        var retiredLookup = await admin.GetAsync("api/catalog/barcodes/lookup?code=" + code); Assert.Equal(HttpStatusCode.Gone, retiredLookup.StatusCode); Assert.Equal("barcode_retired", (string)JObject.Parse(await retiredLookup.Content.ReadAsStringAsync())["error"]["code"]);
                    }
                }
            }
        }
    }
}
