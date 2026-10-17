# Endpoint access policy

Every endpoint must declare its access policy (PRD US-04, M6). Slice 1 has no
authentication yet, so every endpoint that exists today is **anonymous by
design**. Each one carries `EndpointAccess("Anonymous", <rationale>)` metadata
plus `AllowAnonymous`. `EndpointInventoryTests` fails if an endpoint is added
without a classification here and in the test.

| Endpoint | Policy | Why anonymous | What it can reveal |
|---|---|---|---|
| `GET, HEAD /health/live` | Anonymous | Container orchestrators, load balancers and the image `HEALTHCHECK` call it without credentials | `Healthy` only; `Cache-Control: no-store` |
| `GET, HEAD /health/ready` | Anonymous | As above; readiness gates traffic | `Healthy` / `Unhealthy` only. No component names, versions, host names or error text; failure details go to the log (exception text at Debug only) |
| `* /api/{**path}` (unmatched API routes) | Anonymous | Catch-all that returns a Problem Details `404` so unknown API paths never return the SPA shell | Nothing: a generic 404 |
| `GET, HEAD {**path:nonfile}` (SPA shell) | Anonymous | The browser must load the sign-in screen before the user is authenticated | Static HTML with no data |
| Static files (`/assets/*`, `/favicon.svg`) | Anonymous (static file middleware, not an endpoint) | Same as the SPA shell | Compiled front-end code; no data or secrets |

## Plan for slice 3 (authentication and roles)

- A deny-by-default fallback authorization policy is added: an endpoint with
  no policy rejects the request.
- The inventory test becomes the M6 enumeration test: every endpoint must have
  either an authorization policy or `AllowAnonymousByDesign` with a rationale,
  and every non-anonymous endpoint is exercised for `401`/`403`.
- The anonymous endpoints above stay anonymous. The readiness endpoint gains a
  schema-version compatibility check in slice 2 but still returns only status text.
- If a richer health report is ever needed (component status, versions), it
  goes on a separate endpoint that requires the `Admin` role.
