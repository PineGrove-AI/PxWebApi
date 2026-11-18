# PxWebApi plugins

A plugin is a service of its own that PxWebApi offers to its clients. PxWebApi runs none of the plugin's
code: it lists the plugin in `GET /config`, which is how clients such as PxWeb find out that it is there,
and forwards requests under `plugins/{name}/` to wherever the plugin runs. The plugin can be written in
any language and deployed, upgraded and scaled on its own. PxWebApi without plugins behaves exactly as
before.

```
client --GET /config--------------------------> PxWebApi   lists each plugin as a feature
client --GET /plugins/my-search/search?..-----> PxWebApi --forwards--> the plugin
                                                adds the plugin's headers (e.g. its API key)
```

Because clients only ever talk to PxWebApi, a plugin needs no CORS setup, its credentials never reach a
browser, and one PxWebApi URL covers the plugins too.

## Configuring a plugin

Run the plugin somewhere PxWebApi can reach it, and list it in appsettings.json:

```json
"Plugins": [
  {
    "Name": "my-search",
    "Url": "http://my-search:8080/pxweb/",
    "Headers": { "Authorization": "Bearer <the plugin's API key>" },
    "Label": "Smart search",
    "TableSearch": "search"
  }
]
```

| Setting | |
|---|---|
| `Name` | The plugin's id: its route segment, `plugins/{Name}`, and its feature id in `GET /config`. Lowercase letters, digits and dashes. |
| `Url` | Where the plugin is: an absolute http(s) URL. A request for `plugins/{Name}/a/b?x=1` is forwarded to `{Url}a/b?x=1`. |
| `Enabled` | Optional, defaults to `true`. A disabled plugin is neither listed nor forwarded to. |
| `TimeoutSeconds` | Optional, defaults to 30. After that, PxWebApi answers 504. |
| `Headers` | Optional. Added to every request forwarded to the plugin, e.g. its API key. Clients never see them. |
| `Label` | Optional. A short name for the plugin's function for clients to show, e.g. on a search toggle. |
| `TableSearch` | Optional. The path of the plugin's table search endpoint, relative to `Url`. See below. |
| `Params` | Optional. Further parameters for the plugin's feature in `GET /config`, as they are, for clients that know them. |

Settings can come from environment variables as usual, which keeps keys out of the file:
`Plugins__0__Headers__Authorization=Bearer ...`, `Plugins__0__Enabled=false`.

An entry that is wrong (a bad name or URL, two plugins with one name) stops PxWebApi at startup, naming the
entry. PxWebApi doesn't contact the plugin at startup: the plugin may start after it.

## What crosses between client and plugin

PxWebApi forwards `GET`, `HEAD` and `POST`, with the request's body and query string. Only these headers
cross:

- to the plugin: `Accept`, `Accept-Language`, `If-None-Match`, `If-Modified-Since`, `Content-Type`, and the
  plugin's own `Headers`. The client's cookies, credentials and PxWebApi's admin key don't.
- to the client: `Content-Type`, `Content-Length`, `Cache-Control`, `Content-Language`, `ETag`, `Expires`,
  `Last-Modified`, `Vary`. The plugin can't set cookies on PxWebApi's origin.

The plugin's status code reaches the client as it is. If the plugin can't be reached, PxWebApi answers 502;
if it takes longer than `TimeoutSeconds`, 504. PxWebApi follows no redirects from the plugin. Answers go
through PxWebApi's response cache like its own (keyed by method, path and query string).

## How clients find plugins

Each plugin is a feature in `GET /config`:

```json
{
  "id": "my-search",
  "params": [
    { "key": "plugin", "value": "true" },
    { "key": "path", "value": "plugins/my-search" },
    { "key": "tableSearch", "value": "plugins/my-search/search" },
    { "key": "label", "value": "Smart search" }
  ]
}
```

Paths are relative to the API root. `tableSearch` and `label` are there when they are configured, followed
by the plugin's `Params`.

## Table search plugins

A plugin with a `TableSearch` endpoint answers

```
GET {tableSearch}?query={text}&lang={language}
```

with the matching tables, best match first:

```json
{ "tables": [ { "id": "TAB638", "score": 0.93 } ], "note": "optional text to show with the results" }
```

`id` is the table's id as in `GET /tables`; `score` and `note` are optional. The contract is
[table-search.openapi.yaml](table-search.openapi.yaml). PxWeb offers each table search plugin as an
alternative to its keyword search, under the plugin's `label`, and falls back to the keyword search if the
plugin answers with an error.
