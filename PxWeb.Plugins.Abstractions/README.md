# PxWebApi plugins

A plugin is an assembly PxWebApi loads at startup. It can register services and map endpoints of its
own, and it is listed in `GET /config`, which is how clients such as PxWeb find out that it is there.
PxWebApi without plugins behaves exactly as before.

## Writing a plugin

Reference this package, `PxWeb.Plugins.Abstractions`, and implement `IPxWebApiPlugin`:

```csharp
public class MyPlugin : IPxWebApiPlugin
{
    public string Name => "my-plugin";

    public void ConfigureServices(IServiceCollection services, IConfiguration options)
    {
        // options is the plugin's own "Options" section in appsettings.json
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Relative to {RoutePrefix}/plugins/my-plugin
        endpoints.MapGet("hello", () => "Hello");
    }

    public IEnumerable<KeyValuePair<string, string>> GetFeatureParams(string basePath) =>
        [new(PluginFeatureParams.Label, "My plugin")];
}
```

In the plugin's project file:

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="PxWeb.Plugins.Abstractions" Version="1.0.0" ExcludeAssets="runtime" />
</ItemGroup>
```

`EnableDynamicLoading` writes the `.deps.json` PxWebApi resolves the plugin's own dependencies from.
`ExcludeAssets="runtime"` keeps this package out of the plugin's output: PxWebApi has it, and the two
must share one copy. Assemblies PxWebApi already has (ASP.NET Core and PxWebApi's own dependencies)
are always taken from PxWebApi, at PxWebApi's versions.

`PxWeb.Plugins.Sample` is a complete example.

## Configuring plugins

Publish the plugin to a folder PxWebApi can read (e.g. mounted into the container) and list it in
appsettings.json:

```json
"Plugins": [
  {
    "Assembly": "plugins/MyPlugin/MyPlugin.dll",
    "Enabled": true,
    "Options": { "AnySetting": "for the plugin" }
  }
]
```

- `Assembly`: the plugin's main assembly, relative to the content root or absolute. It must have exactly
  one public class implementing `IPxWebApiPlugin`, with a parameterless constructor.
- `Enabled`: optional, defaults to `true`.
- `Options`: optional, passed to the plugin's `ConfigureServices`.

A plugin that can't be loaded stops PxWebApi at startup, naming the entry.

## How clients find plugins

Each plugin is a feature in `GET /config`:

```json
{
  "id": "my-plugin",
  "params": [
    { "key": "plugin", "value": "true" },
    { "key": "path", "value": "plugins/my-plugin" },
    { "key": "label", "value": "My plugin" }
  ]
}
```

`plugin` and `path` are added by PxWebApi; the rest come from `GetFeatureParams`. Paths are relative to
the API root. The keys clients know are in `PluginFeatureParams`.

## Table search plugins

A plugin that offers a table search adds a `tableSearch` param with the path of an endpoint that answers

```
GET {tableSearch}?query={text}&lang={language}
```

with a `TableSearchResponse`, best match first:

```json
{ "tables": [ { "id": "TAB638", "score": 0.93 } ], "note": "optional text to show with the results" }
```

PxWeb offers each table search plugin as an alternative to its keyword search, under the plugin's
`label`, and falls back to the keyword search if the plugin's endpoint fails.
