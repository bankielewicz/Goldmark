# HSTS (HTTP Strict Transport Security)

HSTS tells browsers to use only HTTPS for a host name for a period of time,
and to **refuse to let users click through certificate errors** for that host
name. NFR-SEC-2 requires HSTS; how it is applied is decided in DEC-007.

## Default behaviour

| Certificate in use | `GOLDMARK_HSTS_ENABLED` empty (default) | `true` | `false` |
|---|---|---|---|
| Generated self-signed certificate | **Off** | On | Off |
| Your own certificate (`GOLDMARK_TLS_CERT_FILE`) | **On** | On | Off |

HSTS is off with the generated certificate on purpose. Until users trust that
certificate, the browser shows a warning they can bypass. With HSTS the bypass
disappears, and Goldmark becomes unreachable from every browser that saw the
header until the certificate is trusted or `max-age` expires.

## Settings

| Variable | Default | Meaning |
|---|---|---|
| `GOLDMARK_HSTS_ENABLED` | empty (automatic) | `true` / `false` to force it on or off |
| `GOLDMARK_HSTS_MAX_AGE_DAYS` | `365` | `max-age` in days (1–730) |

The header is sent as `Strict-Transport-Security: max-age=<seconds>`.

- `includeSubDomains` is **off** by default (`Goldmark:Https:Hsts:IncludeSubDomains`).
  Turning it on for `cmdb.corp.example` would force HTTPS on every
  `*.cmdb.corp.example` host, which is rarely what you want. It never affects
  the parent domain.
- `preload` is **never** sent. Preloading is for public sites and is hard to undo.
- ASP.NET Core does not send HSTS to `localhost`, `127.0.0.1` or `[::1]`.

## Rolling it out

1. Install a certificate that every client trusts (see [tls-certificates.md](tls-certificates.md)).
2. Optionally start with a short `max-age`, e.g. `GOLDMARK_HSTS_MAX_AGE_DAYS=1`,
   and check that nobody gets certificate errors.
3. Raise it to 365 days.

Turning HSTS off later does not clear it from browsers that already received
it; they keep enforcing HTTPS until `max-age` runs out. That is harmless as
long as Goldmark stays on HTTPS with a trusted certificate.

## Behind a reverse proxy

If a reverse proxy or load balancer terminates TLS in front of Goldmark, set
HSTS on the proxy and `GOLDMARK_HSTS_ENABLED=false` on Goldmark, so the policy
lives in one place. Forwarded-header support for proxies is not implemented
yet.
