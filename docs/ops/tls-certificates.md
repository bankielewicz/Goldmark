# TLS certificates

Goldmark serves **HTTPS only** (US-01). There is no plain-HTTP listener and no
HTTP-to-HTTPS redirect: browsers and API clients must use `https://`.

There are two certificates in a Compose deployment:

| Certificate | Used for | Created by | Stored in |
|---|---|---|---|
| Goldmark HTTPS certificate | Browsers and API clients connecting to Goldmark | The app, on first start (or you supply your own) | Volume `goldmark-tls` (`/var/lib/goldmark/tls` in the app container) |
| SQL Server certificate | The internal Goldmark → SQL Server connection (TDS 8 strict) | The `sqlserver` container, on start | Volume `sql-tls-private` (key) and `sql-tls-public` (public certificate only) |

You normally only care about the first one.

## The generated self-signed certificate

On first start, if you have not supplied a certificate, Goldmark generates a
per-installation certificate:

- ECDSA P-256, SHA-256, valid for 397 days, server-authentication only.
- Subject alternative names come from `GOLDMARK_TLS_HOSTNAMES` (DNS names and IP
  addresses, comma-separated). Put every name users will type in the browser
  there, for example `GOLDMARK_TLS_HOSTNAMES=cmdb.corp.example,cmdb,10.20.30.40`.
- The private key is written with mode `0600` in a `0700` directory owned by
  the app user (uid 1654). It is never part of the image.
- It is reused on every restart and container re-creation. It is regenerated
  automatically when it is within 30 days of expiry, when `GOLDMARK_TLS_HOSTNAMES`
  changes, or when the files are unreadable. Each regeneration logs a warning,
  and clients that trusted the old certificate must trust the new one.
- **Restart within the renewal window.** The expiry check runs when the app
  starts. A container that keeps running past the certificate's expiry keeps
  serving the expired certificate, and the container health check (which pins
  the certificate on disk) does not notice. Restart the app at least once in
  the last 30 days of the certificate's life (the expiry date is logged at
  start-up with the fingerprint); any routine update or host reboot does this.
- **Windows** (the Windows service, `%ProgramData%\Goldmark\tls`, and dev hosts,
  `.goldmark/tls` under the project when run with `dotnet run`): Goldmark creates
  the folder readable by SYSTEM, Administrators and the account it runs as, with
  inheritance off (DEC-043, #6). A folder that already exists and inherits its
  permissions or grants anyone else is refused at start, naming
  `Goldmark:Https:GeneratedCertificateDirectory`: remove it (Goldmark generates a
  new certificate) or restrict it.

The SHA-256 fingerprint is logged at start-up (event 2003 or 2005), so you can
compare it with what your browser shows:

```sh
docker compose -f deploy/docker-compose.yml logs app | grep -m1 Fingerprint
```

### Trusting the generated certificate

Browsers warn about a self-signed certificate until you trust it. Export the
public certificate (it contains no secret):

```sh
docker compose -f deploy/docker-compose.yml cp app:/var/lib/goldmark/tls/goldmark.crt ./goldmark.crt
openssl x509 -in goldmark.crt -noout -fingerprint -sha256   # must match the logged fingerprint
```

Then import `goldmark.crt` as a trusted certificate on the client machines:

- **Windows (Edge, Chrome):** `certutil -addstore -user Root goldmark.crt`
  (per user) or deploy it to *Trusted Root Certification Authorities* via
  Group Policy.
- **macOS:** open it in Keychain Access, add it to the *login* or *System*
  keychain and set *When using this certificate* to *Always Trust*.
- **Linux:** copy it to `/usr/local/share/ca-certificates/goldmark.crt` and run
  `sudo update-ca-certificates`.
- **Firefox:** *Settings → Privacy & Security → Certificates → View
  Certificates → Authorities → Import*, or set
  `security.enterprise_roots.enabled` to use the OS store.

Trusting a self-signed certificate is reasonable for a pilot. For production,
use a certificate from your organization's CA (below).

## Using your own certificate

Supply a PEM certificate and an **unencrypted** PEM private key (PKCS#8,
PKCS#1 or SEC1). The certificate file may contain the leaf certificate
followed by intermediate CA certificates; Goldmark sends the chain to clients.

1. Put the files on the Docker host, readable by uid 1654 (for example
   `chown 1654:1654 server.key && chmod 600 server.key`).
2. Mount them with a Compose override file next to `deploy/docker-compose.yml`,
   for example `deploy/docker-compose.override.yml`:

   ```yaml
   services:
     app:
       volumes:
         - /etc/goldmark/tls:/etc/goldmark/tls:ro
   ```

3. In `deploy/.env`:

   ```sh
   GOLDMARK_TLS_CERT_FILE=/etc/goldmark/tls/server.crt
   GOLDMARK_TLS_KEY_FILE=/etc/goldmark/tls/server.key
   ```

4. `docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.override.yml up -d`

With your own certificate, HSTS turns on automatically; read
[hsts.md](hsts.md) first. To renew, replace the files and restart the app
container. An expired certificate is logged as an error at start-up. The Windows
service reads copies the installer made: see
[windows-service.md](windows-service.md#renewing-certificates).

## The SQL Server certificate

The bundled SQL Server creates its own certificate for the host name
`sqlserver` (only valid inside the Compose network) and is configured to
accept **only** TDS 8 strict-encrypted connections. Goldmark and the database
bootstrap pin that exact certificate (`ServerCertificate`); nothing uses
`TrustServerCertificate`. The private key stays in the `sql-tls-private`
volume, which only the `sqlserver` container mounts. It is regenerated within
30 days of expiry; Goldmark picks the new public certificate up from the
shared volume automatically.
