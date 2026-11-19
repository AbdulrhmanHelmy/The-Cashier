using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using AlKhayal.Server.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AlKhayal.IntegrationTests
{
    public sealed class StandaloneFirstRunApiTests
    {
        [Fact]
        public async Task Fresh_host_serves_Cashier_login_and_persists_bootstrap_and_Arabic_catalog_after_restart()
        {
            using (var database = new TreasuryPersistenceTests.Fixture())
            {
                // Use a fresh database path so host startup, rather than the fixture, applies migrations.
                var path = Path.Combine(Path.GetDirectoryName(database.DatabasePath), "first-run.db");
                var frontend = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../frontend"));
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var url = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
                listener.Stop();
                var options = new HostOptions { DatabasePath = path, FrontendPath = frontend, Url = url.ToString().TrimEnd('/') };
                const string user = "cashier-qa-admin";
                var password = Guid.NewGuid().ToString("N");
                const string name = "صنف اختبار الكاشير";
                long productId;

                using (var host = new AlKhayalServer(new TestLicenseGate()))
                using (var client = new HttpClient { BaseAddress = url })
                {
                    host.Start(options);
                    Assert.Contains("The Cashier", await client.GetStringAsync("login.html"));
                    var bootstrap = JObject.Parse(await client.GetStringAsync("api/auth/bootstrap-status"));
                    Assert.True((bool)bootstrap["data"]["required"]);
                    var admin = await Post(client, "api/auth/bootstrap", new { username = user, password, confirmPassword = password }, HttpStatusCode.Created);
                    var product = await Post(client, "api/catalog/products", new
                    {
                        operationKey = "first-run-catalog",
                        name,
                        baseUnitId = 1,
                        productType = "stock",
                        salePrice = "14.12",
                        purchasePrice = "10.33",
                        isActive = true
                    }, HttpStatusCode.Created, (string)admin["csrfToken"]);
                    productId = (long)product["id"];
                    await Post(client, "api/auth/logout", new { }, HttpStatusCode.OK, (string)admin["csrfToken"]);
                    using (var anonymous = await client.GetAsync("api/auth/me"))
                        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
                }

                using (var host = new AlKhayalServer(new TestLicenseGate()))
                using (var client = new HttpClient { BaseAddress = url })
                {
                    host.Start(options);
                    var bootstrap = JObject.Parse(await client.GetStringAsync("api/auth/bootstrap-status"));
                    Assert.False((bool)bootstrap["data"]["required"]);
                    await Post(client, "api/auth/login", new { username = user, password }, HttpStatusCode.OK);
                    var detail = JObject.Parse(await client.GetStringAsync("api/catalog/products/" + productId))["data"];
                    Assert.Equal(name, (string)detail["name"]);
                    Assert.Equal("14.12", (string)detail["salePrice"]);
                    Assert.Equal("10.33", (string)detail["purchasePrice"]);
                }
            }
        }

        private static async Task<JToken> Post(HttpClient client, string path, object body, HttpStatusCode expected, string csrf = null)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, path))
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                if (csrf != null) request.Headers.Add("X-CSRF-Token", csrf);
                using (var response = await client.SendAsync(request))
                {
                    var result = JObject.Parse(await response.Content.ReadAsStringAsync());
                    Assert.True(response.StatusCode == expected, path + " returned " + response.StatusCode + ": " + (string)result["error"]?["code"]);
                    return result["data"];
                }
            }
        }
    }
}
