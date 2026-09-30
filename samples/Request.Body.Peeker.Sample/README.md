# Request.Body.Peeker sample

A minimal API that consumes the published [Request.Body.Peeker](https://www.nuget.org/packages/Request.Body.Peeker/) 2.0.0 package from nuget.org.

It demonstrates the typical use case: middleware inspects the request body before the endpoint binds it.

- A middleware on `POST /orders` peeks the body as `OrderRequest` with a 64 KB `BufferLimit`, rejects oversized bodies with 413, malformed JSON with 400, and a `tenantId` that does not match the `X-Tenant` header with 403. Otherwise it stores the peeked order in `HttpContext.Items` and calls the next middleware.
- The `POST /orders` endpoint still binds `OrderRequest` from the body and returns both the peeked and the bound order, plus the `Content-Length` header (`null` for chunked requests).
- `POST /echo` peeks the body as a string, then reads it a second time with a `StreamReader` to show the stream was rewound.

## Run

```bash
ASPNETCORE_URLS=http://127.0.0.1:5188 dotnet run --project samples/Request.Body.Peeker.Sample -c Release
```

## Try it

Valid order (200, `peekedByMiddleware` and `boundByEndpoint` both filled, `contentLengthHeader` 44):

```bash
curl -s -w '\n%{http_code}\n' -X POST http://127.0.0.1:5188/orders -H 'Content-Type: application/json' -H 'X-Tenant: acme' -d '{"tenantId":"acme","sku":"A-1","quantity":2}'
```

Chunked order (200, `contentLengthHeader` null):

```bash
curl -s -w '\n%{http_code}\n' -X POST http://127.0.0.1:5188/orders -H 'Content-Type: application/json' -H 'X-Tenant: acme' -H 'Transfer-Encoding: chunked' -d '{"tenantId":"acme","sku":"A-1","quantity":2}'
```

Tenant mismatch (403):

```bash
curl -s -w '\n%{http_code}\n' -X POST http://127.0.0.1:5188/orders -H 'Content-Type: application/json' -H 'X-Tenant: other' -d '{"tenantId":"acme","sku":"A-1","quantity":2}'
```

Malformed JSON (400):

```bash
curl -s -w '\n%{http_code}\n' -X POST http://127.0.0.1:5188/orders -H 'Content-Type: application/json' -H 'X-Tenant: acme' -d '{"tenantId":'
```

Body over the 64 KB `BufferLimit` (413):

```bash
{ printf '{"tenantId":"acme","sku":"'; head -c 70000 /dev/zero | tr '\0' 'a'; printf '","quantity":1}'; } > body.json
curl -s -w '\n%{http_code}\n' -X POST http://127.0.0.1:5188/orders -H 'Content-Type: application/json' -H 'X-Tenant: acme' --data-binary @body.json
```

Echo with a chunked body (the text, then `-- second read: 21 chars`):

```bash
curl -s -X POST http://127.0.0.1:5188/echo -H 'Content-Type: text/plain' -H 'Transfer-Encoding: chunked' -d 'hello from the peeker'
```
