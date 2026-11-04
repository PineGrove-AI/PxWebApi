using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using PxWeb;

namespace PxWebApi_Mvc.Tests
{
    /// <summary>
    /// Plugins loaded from disk, as in a deployment: the sample plugin (PxWeb.Plugins.Sample), built
    /// next to this project but not referenced by it, so it only reaches PxWebApi through its own load context.
    /// </summary>
    [TestClass]
    public class PluginTests
    {
        private static readonly string SamplePluginPath = typeof(PluginTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "SamplePluginPath").Value!;

        private static WebApplicationFactory<Program> WithSamplePlugin(bool enabled = true, string? assembly = null) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Plugins:0:Assembly", assembly ?? SamplePluginPath);
                builder.UseSetting("Plugins:0:Enabled", enabled.ToString());
                builder.UseSetting("Plugins:0:Options:Label", "Sample search");
                builder.UseSetting("Plugins:0:Options:Tables:0:Id", "TAB001");
                builder.UseSetting("Plugins:0:Options:Tables:0:Title", "Population by region and year");
                builder.UseSetting("Plugins:0:Options:Tables:1:Id", "TAB002");
                builder.UseSetting("Plugins:0:Options:Tables:1:Title", "Population by sex");
            });

        [TestMethod]
        public async Task ConfigListsThePluginAsAFeature()
        {
            await using var application = WithSamplePlugin();
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
        public async Task ThePluginsEndpointsAreMappedUnderItsPath()
        {
            await using var application = WithSamplePlugin();
            using var client = application.CreateClient();

            var response = JsonNode.Parse(await client.GetStringAsync("/plugins/sample/search?query=population%20region&lang=en"))!;

            var ids = response["tables"]!.AsArray().Select(t => t!["id"]!.GetValue<string>()).ToList();
            CollectionAssert.AreEqual(new[] { "TAB001" }, ids);
            Assert.AreEqual("1 of 2 tables", response["note"]!.GetValue<string>());
        }

        [TestMethod]
        public async Task ADisabledPluginIsNotLoaded()
        {
            await using var application = WithSamplePlugin(enabled: false);
            using var client = application.CreateClient();

            var config = JsonNode.Parse(await client.GetStringAsync("/config"))!;

            Assert.IsFalse(config["features"]!.AsArray().Any(f => f!["id"]!.GetValue<string>() == "sample"));
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/plugins/sample/search?query=population")).StatusCode);
        }

        [TestMethod]
        public void AMissingPluginAssemblyStopsStartup()
        {
            using var application = WithSamplePlugin(assembly: "plugins/does-not-exist.dll");

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => application.CreateClient());

            StringAssert.Contains(ex.Message, "does-not-exist.dll");
        }
    }
}
