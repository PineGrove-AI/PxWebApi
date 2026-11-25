using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

using PxWeb;

namespace PxWebApi_Mvc.Tests
{
    [TestClass]
    public class PluginTests
    {
        private const string PluginUrl = "http://plugin.test/api/";

        private sealed class FakePluginHandler : HttpMessageHandler
        {
            public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

            public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = _ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"tables":[{"id":"TAB001","score":0.9}]}""", Encoding.UTF8, "application/json"),
                });

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add((request, body));
                return await Respond(cancellationToken);
            }
        }

        private static WebApplicationFactory<Program> CreateApplication(FakePluginHandler handler, bool enabled = true, string url = PluginUrl, int timeoutSeconds = 30)
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Plugins:0:Name", "sample");
                builder.UseSetting("Plugins:0:Url", url);
                builder.UseSetting("Plugins:0:Enabled", enabled.ToString());
                builder.UseSetting("Plugins:0:TimeoutSeconds", timeoutSeconds.ToString());
                builder.UseSetting("Plugins:0:Headers:Authorization", "Bearer plugin-key");
                builder.UseSetting("Plugins:0:Labels:en", "Sample search");
                builder.UseSetting("Plugins:0:Capabilities:0:Type", "tableSearch");
                builder.UseSetting("Plugins:0:Capabilities:0:Path", "search");
                builder.ConfigureTestServices(services =>
                    services.ConfigureAll<HttpClientFactoryOptions>(options =>
                        options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler)));
            });
        }

        [TestMethod]
        public async Task GetConfig_ListsPlugin()
        {
            await using var application = CreateApplication(new FakePluginHandler());
            using var client = application.CreateClient();

            var config = JsonNode.Parse(await client.GetStringAsync("/config"))!;

            var plugin = config["plugins"]!.AsArray().Single()!;
            Assert.AreEqual("sample", plugin["id"]!.GetValue<string>());
            Assert.AreEqual("plugins/sample", plugin["path"]!.GetValue<string>());
            Assert.AreEqual("en", plugin["labels"]![0]!["language"]!.GetValue<string>());
            Assert.AreEqual("Sample search", plugin["labels"]![0]!["text"]!.GetValue<string>());
            Assert.AreEqual("tableSearch", plugin["capabilities"]![0]!["type"]!.GetValue<string>());
            Assert.AreEqual("plugins/sample/search", plugin["capabilities"]![0]!["path"]!.GetValue<string>());
        }

        [TestMethod]
        public async Task Get_IsForwardedToPlugin()
        {
            var handler = new FakePluginHandler();
            await using var application = CreateApplication(handler);
            using var client = application.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/plugins/sample/search?query=population%20region&lang=en");
            request.Headers.Add("Cookie", "session=secret");
            request.Headers.Add("API_ADMIN_KEY", "admin-secret");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "client-token");
            request.Headers.AcceptLanguage.ParseAdd("en");

            using var response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            Assert.AreEqual("TAB001", body["tables"]![0]!["id"]!.GetValue<string>());

            var forwarded = handler.Requests.Single().Request;
            Assert.AreEqual(PluginUrl + "search?query=population%20region&lang=en", forwarded.RequestUri!.AbsoluteUri);
            Assert.AreEqual("Bearer plugin-key", forwarded.Headers.Authorization?.ToString());
            Assert.AreEqual("en", forwarded.Headers.AcceptLanguage.ToString());
            Assert.IsFalse(forwarded.Headers.Contains("Cookie"));
            Assert.IsFalse(forwarded.Headers.Contains("API_ADMIN_KEY"));
        }

        [TestMethod]
        public async Task Post_IsForwardedWithBody()
        {
            var handler = new FakePluginHandler();
            await using var application = CreateApplication(handler);
            using var client = application.CreateClient();

            using var response = await client.PostAsync("/plugins/sample/feedback", new StringContent("""{"useful":true}""", Encoding.UTF8, "application/json"));

            var (forwarded, body) = handler.Requests.Single();
            Assert.AreEqual(HttpMethod.Post, forwarded.Method);
            Assert.AreEqual(PluginUrl + "feedback", forwarded.RequestUri!.AbsoluteUri);
            Assert.AreEqual("""{"useful":true}""", body);
        }

        [TestMethod]
        public async Task Get_PathOutsidePlugin_ReturnsBadRequest()
        {
            var handler = new FakePluginHandler();
            await using var application = CreateApplication(handler);

            var context = await application.Server.SendAsync(c =>
            {
                c.Request.Method = HttpMethods.Get;
                c.Request.Path = "/plugins/sample/../admin";
            });

            Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        public async Task Get_PluginUnreachable_ReturnsBadGateway()
        {
            var handler = new FakePluginHandler { Respond = _ => throw new HttpRequestException("Connection refused") };
            await using var application = CreateApplication(handler);
            using var client = application.CreateClient();

            using var response = await client.GetAsync("/plugins/sample/search?query=x");

            Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        }

        [TestMethod]
        public async Task Get_PluginTimesOut_ReturnsGatewayTimeout()
        {
            var handler = new FakePluginHandler
            {
                Respond = async ct =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                },
            };
            await using var application = CreateApplication(handler, timeoutSeconds: 1);
            using var client = application.CreateClient();

            using var response = await client.GetAsync("/plugins/sample/search?query=x");

            Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        }

        [TestMethod]
        public async Task DisabledPlugin_IsNotListedOrForwarded()
        {
            var handler = new FakePluginHandler();
            await using var application = CreateApplication(handler, enabled: false);
            using var client = application.CreateClient();

            var config = JsonNode.Parse(await client.GetStringAsync("/config"))!;
            using var response = await client.GetAsync("/plugins/sample/search?query=x");

            Assert.IsNull(config["plugins"]);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        public void InvalidUrl_FailsAtStartup()
        {
            using var application = CreateApplication(new FakePluginHandler(), url: "not-a-url");

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => application.CreateClient());

            StringAssert.Contains(ex.Message, "Plugins:0");
        }
    }
}
