using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

using PxWeb;

namespace PxWebApi_Mvc.Tests
{
    /// <summary>
    /// A plugin as a deployment configures one, with the plugin itself played by <see cref="FakePlugin"/>:
    /// every request PxWebApi forwards to it reaches the fake instead of the network.
    /// </summary>
    [TestClass]
    public class PluginTests
    {
        private const string PluginUrl = "http://search.test/pxweb/";

        /// <summary>Records the requests it gets and answers them with <see cref="Answer"/>.</summary>
        private sealed class FakePlugin : HttpMessageHandler
        {
            public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

            public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } = (_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"tables":[{"id":"TAB001","score":0.9}],"note":"population"}""", Encoding.UTF8, "application/json"),
                });

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add((request, body));
                return await Answer(request, cancellationToken);
            }
        }

        private static WebApplicationFactory<Program> WithPlugin(FakePlugin plugin, bool enabled = true, string url = PluginUrl, int timeoutSeconds = 30) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Plugins:0:Name", "sample");
                builder.UseSetting("Plugins:0:Url", url);
                builder.UseSetting("Plugins:0:Enabled", enabled.ToString());
                builder.UseSetting("Plugins:0:TimeoutSeconds", timeoutSeconds.ToString());
                builder.UseSetting("Plugins:0:Headers:Authorization", "Bearer plugin-key");
                builder.UseSetting("Plugins:0:Label", "Sample search");
                builder.UseSetting("Plugins:0:TableSearch", "search");
                builder.ConfigureTestServices(services =>
                    services.ConfigureAll<HttpClientFactoryOptions>(options =>
                        options.HttpMessageHandlerBuilderActions.Add(handlers => handlers.PrimaryHandler = plugin)));
            });

        [TestMethod]
        public async Task ConfigListsThePluginAsAFeature()
        {
            await using var application = WithPlugin(new FakePlugin());
            using var client = application.CreateClient();

            var config = JsonNode.Parse(await client.GetStringAsync("/config"))!;

            var feature = config["features"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == "sample")!;
            var featureParams = feature["params"]!.AsArray()
                .ToDictionary(p => p!["key"]!.GetValue<string>(), p => p!["value"]!.GetValue<string>());
            Assert.AreEqual("true", featureParams["plugin"]);
            Assert.AreEqual("plugins/sample", featureParams["path"]);
            Assert.AreEqual("plugins/sample/search", featureParams["tableSearch"]);
            Assert.AreEqual("Sample search", featureParams["label"]);
        }

        [TestMethod]
        public async Task ARequestUnderThePluginsPathIsForwardedToThePlugin()
        {
            var plugin = new FakePlugin();
            await using var application = WithPlugin(plugin);
            using var client = application.CreateClient();

            using var response = await client.GetAsync("/plugins/sample/search?query=population%20region&lang=da");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            Assert.AreEqual("TAB001", body["tables"]![0]!["id"]!.GetValue<string>());

            var forwarded = plugin.Requests.Single().Request;
            Assert.AreEqual(HttpMethod.Get, forwarded.Method);
            Assert.AreEqual(PluginUrl + "search?query=population%20region&lang=da", forwarded.RequestUri!.AbsoluteUri);
        }

        [TestMethod]
        public async Task ThePluginGetsItsOwnHeadersButNotTheClientsCredentials()
        {
            var plugin = new FakePlugin();
            await using var application = WithPlugin(plugin);
            using var client = application.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/plugins/sample/search?query=x");
            request.Headers.Add("Cookie", "session=secret");
            request.Headers.Add("API_ADMIN_KEY", "admin-secret");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "client-token");
            request.Headers.AcceptLanguage.ParseAdd("da");

            using var _ = await client.SendAsync(request);

            var forwarded = plugin.Requests.Single().Request;
            Assert.AreEqual("Bearer plugin-key", forwarded.Headers.Authorization?.ToString());
            Assert.AreEqual("da", forwarded.Headers.AcceptLanguage.ToString());
            Assert.IsFalse(forwarded.Headers.Contains("Cookie"));
            Assert.IsFalse(forwarded.Headers.Contains("API_ADMIN_KEY"));
        }

        [TestMethod]
        public async Task APostIsForwardedWithItsBody()
        {
            var plugin = new FakePlugin();
            await using var application = WithPlugin(plugin);
            using var client = application.CreateClient();

            using var _ = await client.PostAsync("/plugins/sample/feedback", new StringContent("""{"good":true}""", Encoding.UTF8, "application/json"));

            var (forwarded, body) = plugin.Requests.Single();
            Assert.AreEqual(HttpMethod.Post, forwarded.Method);
            Assert.AreEqual(PluginUrl + "feedback", forwarded.RequestUri!.AbsoluteUri);
            Assert.AreEqual("""{"good":true}""", body);
            Assert.AreEqual("application/json", forwarded.Content!.Headers.ContentType?.MediaType);
        }

        [TestMethod]
        public async Task ThePluginsErrorsReachTheClientAsTheyAre()
        {
            var plugin = new FakePlugin { Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) };
            await using var application = WithPlugin(plugin);
            using var client = application.CreateClient();

            using var response = await client.GetAsync("/plugins/sample/search?query=x");

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        [TestMethod]
        public async Task APluginThatCantBeReachedIsABadGateway()
        {
            var plugin = new FakePlugin { Answer = (_, _) => throw new HttpRequestException("Connection refused") };
            await using var application = WithPlugin(plugin);
            using var client = application.CreateClient();

            using var response = await client.GetAsync("/plugins/sample/search?query=x");

            Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        }

        [TestMethod]
        public async Task APluginThatTakesTooLongIsAGatewayTimeout()
        {
            var plugin = new FakePlugin
            {
                Answer = async (_, ct) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                },
            };
            await using var application = WithPlugin(plugin, timeoutSeconds: 1);
            using var client = application.CreateClient();

            using var response = await client.GetAsync("/plugins/sample/search?query=x");

            Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        }

        [TestMethod]
        public async Task ADisabledPluginIsNeitherListedNorForwardedTo()
        {
            var plugin = new FakePlugin();
            await using var application = WithPlugin(plugin, enabled: false);
            using var client = application.CreateClient();

            var config = JsonNode.Parse(await client.GetStringAsync("/config"))!;

            Assert.IsFalse(config["features"]!.AsArray().Any(f => f!["id"]!.GetValue<string>() == "sample"));
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/plugins/sample/search?query=x")).StatusCode);
            Assert.IsEmpty(plugin.Requests);
        }

        [TestMethod]
        public void APluginWithoutAUsableUrlStopsStartup()
        {
            using var application = WithPlugin(new FakePlugin(), url: "not-a-url");

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => application.CreateClient());

            StringAssert.Contains(ex.Message, "Plugins:0");
        }
    }
}
