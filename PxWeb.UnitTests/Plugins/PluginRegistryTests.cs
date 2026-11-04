using System;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PxWeb.Code.Plugins;
using PxWeb.Plugins;

namespace PxWeb.UnitTests.Plugins
{
    [TestClass]
    public class PluginRegistryTests
    {
        private sealed class TestPlugin(string name, params KeyValuePair<string, string>[] featureParams) : IPxWebApiPlugin
        {
            public string Name => name;

            public void ConfigureServices(IServiceCollection services, IConfiguration options) { }

            public void MapEndpoints(IEndpointRouteBuilder endpoints) { }

            public IEnumerable<KeyValuePair<string, string>> GetFeatureParams(string basePath) =>
                featureParams.Select(p => new KeyValuePair<string, string>(p.Key, p.Value.Replace("{base}", basePath)));
        }

        [TestMethod]
        public void GetFeatures_ListsEachPluginWithItsPathAndOwnParams()
        {
            var registry = new PluginRegistry([new TestPlugin("table-search", new KeyValuePair<string, string>(PluginFeatureParams.TableSearch, "{base}/search"))]);

            var feature = registry.GetFeatures().Single();

            Assert.AreEqual("table-search", feature.Id);
            var featureParams = feature.Params.ToDictionary(p => p.Key, p => p.Value);
            Assert.AreEqual("true", featureParams[PluginFeatureParams.Plugin]);
            Assert.AreEqual("plugins/table-search", featureParams[PluginFeatureParams.Path]);
            Assert.AreEqual("plugins/table-search/search", featureParams[PluginFeatureParams.TableSearch]);
        }

        [TestMethod]
        public void GetFeatures_NoPlugins_NoFeatures()
        {
            Assert.IsEmpty(new PluginRegistry([]).GetFeatures());
        }

        [TestMethod]
        [DataRow("Upper")]
        [DataRow("with space")]
        [DataRow("-leading-dash")]
        [DataRow("")]
        public void Constructor_NameThatIsNotARouteSegment_Throws(string name)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => new PluginRegistry([new TestPlugin(name)]));
        }

        [TestMethod]
        public void Constructor_TwoPluginsWithTheSameName_Throws()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => new PluginRegistry([new TestPlugin("same"), new TestPlugin("same")]));
        }
    }
}
