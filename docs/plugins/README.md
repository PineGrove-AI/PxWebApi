# Plugins

A plugin is an external HTTP service that PxWebApi makes available to its clients. PxWebApi lists each
plugin as a feature in `GET /config` and forwards requests under `plugins/{name}/` to the plugin's URL.
No plugin code runs inside PxWebApi.

Since clients only talk to PxWebApi, plugins need no CORS configuration and any credentials for the
plugin stay on the server.

## Configuration

```json
"Plugins": [
  {
    "Name": "my-search",
    "Url": "http://my-search:8080/pxweb/",
    "Headers": { "Authorization": "Bearer <key>" },
    "Label": "Smart search",
    "TableSearch": "search"
  }
]
```

| Setting | Description |
|---|---|
| `Name` | Route segment and feature id. Lowercase letters, digits and dashes. |
| `Url` | Base URL of the plugin. `plugins/{Name}/a/b?x=1` is forwarded to `{Url}a/b?x=1`. |
| `Enabled` | Optional, default `true`. |
| `TimeoutSeconds` | Optional, default `30`. PxWebApi returns 504 when exceeded. |
| `Headers` | Optional. Added to every request sent to the plugin. |
| `Label` | Optional. Name for clients to display. |
| `TableSearch` | Optional. Path of a table search endpoint, relative to `Url`. |
| `Params` | Optional. Extra parameters for the feature in `GET /config`. |

An invalid entry stops PxWebApi at startup. Settings can also be given as environment variables, for
example `Plugins__0__Headers__Authorization`.

## Forwarding

`GET`, `HEAD` and `POST` are forwarded with the query string and body. Only these headers are passed on:

- To the plugin: `Accept`, `Accept-Language`, `Content-Type`, `If-None-Match`, `If-Modified-Since` and the
  configured `Headers`.
- To the client: `Content-Type`, `Content-Length`, `Cache-Control`, `Content-Language`, `ETag`, `Expires`,
  `Last-Modified` and `Vary`.

The plugin's status code is returned as is. PxWebApi returns 502 if the plugin cannot be reached and 504
on timeout. Redirects are not followed.

## Discovery

Each plugin is listed in `GET /config`:

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

Paths are relative to the API root.

## Table search

A plugin with `TableSearch` set answers `GET {tableSearch}?query={text}&lang={language}` with matching
tables, best match first:

```json
{ "tables": [ { "id": "TAB638", "score": 0.93 } ], "note": "Optional text shown with the results" }
```

See [table-search.openapi.yaml](table-search.openapi.yaml). PxWeb offers each table search plugin as an
alternative to its keyword search and falls back to keyword search if the plugin returns an error.
