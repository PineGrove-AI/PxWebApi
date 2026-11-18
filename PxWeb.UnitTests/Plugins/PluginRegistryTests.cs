using System;

using Microsoft.AspNetCore.Http;

using PxWeb.Code.Plugins;

namespace PxWeb.UnitTests.Plugins
{
    [TestClass]
    public class PluginRegistryTests
    {
        private static PluginRegistry Registry(params PluginOptions[] plugins) =>
            new(plugins.Select((options, i) => ($"Plugins:{i}", options)));

        private static PluginOptions Options(string name = "table-search", string url = "http://search.test/pxweb") =>
            new() { Name = name, Url = url };

        [TestMethod]
        public void GetFeatures_ListsEachPluginWithItsPathsAndParams()
        {
            var options = Options();
            options.Label = "Smart search";
            options.TableSearch = "search";
            options.Params["icon"] = "sparkle";

            var feature = Registry(options).GetFeatures().Single();

            Assert.AreEqual("table-search", feature.Id);
            var featureParams = feature.Params.ToDictionary(p => p.Key, p => p.Value);
            Assert.AreEqual("true", featureParams[PluginFeatureParams.Plugin]);
            Assert.AreEqual("plugins/table-search", featureParams[PluginFeatureParams.Path]);
            Assert.AreEqual("plugins/table-search/search", featureParams[PluginFeatureParams.TableSearch]);
            Assert.AreEqual("Smart search", featureParams[PluginFeatureParams.Label]);
            Assert.AreEqual("sparkle", featureParams["icon"]);
        }

        [TestMethod]
        public void GetFeatures_NoTableSearchOrLabel_NeitherParam()
        {
            var featureParams = Registry(Options()).GetFeatures().Single().Params.Select(p => p.Key).ToList();

            CollectionAssert.AreEquivalent(new[] { PluginFeatureParams.Plugin, PluginFeatureParams.Path }, featureParams);
        }

        [TestMethod]
        public void GetFeatures_NoPlugins_NoFeatures()
        {
            Assert.IsEmpty(Registry().GetFeatures());
        }

        [TestMethod]
        [DataRow("http://search.test/pxweb", "http://search.test/pxweb/")]
        [DataRow("http://search.test/pxweb/", "http://search.test/pxweb/")]
        [DataRow("https://search.test", "https://search.test/")]
        public void Constructor_UrlIsAFolder(string url, string upstream)
        {
            Assert.AreEqual(upstream, Registry(Options(url: url)).Plugins.Single().Upstream.AbsoluteUri);
        }

        [TestMethod]
        [DataRow("Upper")]
        [DataRow("with space")]
        [DataRow("-leading-dash")]
        [DataRow("")]
        public void Constructor_NameThatIsNotARouteSegment_Throws(string name)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => Registry(Options(name: name)));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("search.test/pxweb")]
        [DataRow("ftp://search.test/")]
        [DataRow("http://search.test/pxweb?key=1")]
        public void Constructor_UrlThatIsNotAnHttpFolder_Throws(string url)
        {
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => Registry(Options(url: url)));

            StringAssert.StartsWith(ex.Message, "Plugins:0:");
        }

        [TestMethod]
        [DataRow("../admin")]
        [DataRow("http://elsewhere.test/search")]
        [DataRow("search?x=1")]
        public void Constructor_TableSearchOutsideTheUrl_Throws(string tableSearch)
        {
            var options = Options();
            options.TableSearch = tableSearch;

            Assert.ThrowsExactly<InvalidOperationException>(() => Registry(options));
        }

        [TestMethod]
        public void Constructor_ParamsThatPxWebApiSets_Throws()
        {
            var options = Options();
            options.Params[PluginFeatureParams.Path] = "elsewhere";

            Assert.ThrowsExactly<InvalidOperationException>(() => Registry(options));
        }

        [TestMethod]
        public void Constructor_TwoPluginsWithTheSameName_Throws()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => Registry(Options("same"), Options("same")));
        }

        [TestMethod]
        [DataRow("/plugins/table-search/search", "?query=a%20b&lang=da", "http://search.test/pxweb/search?query=a%20b&lang=da")]
        [DataRow("/plugins/table-search", "", "http://search.test/pxweb/")]
        [DataRow("/plugins/table-search/a/b", "", "http://search.test/pxweb/a/b")]
        public void TargetUri_AddsTheRestOfThePathAndTheQueryToTheUrl(string path, string query, string expected)
        {
            var request = new DefaultHttpContext().Request;
            request.Path = path;
            request.QueryString = new QueryString(query);

            Assert.AreEqual(expected, Registry(Options()).Plugins.Single().TargetUri(request)?.AbsoluteUri);
        }

        [TestMethod]
        [DataRow("/plugins/table-search/../admin")]
        [DataRow("/plugins/table-search/a/../../admin")]
        [DataRow("/plugins/table-search/./x")]
        [DataRow("/plugins/other/search")]
        public void TargetUri_PathThatLeavesTheUrl_Null(string path)
        {
            var request = new DefaultHttpContext().Request;
            request.Path = path;

            Assert.IsNull(Registry(Options()).Plugins.Single().TargetUri(request));
        }
    }
}
