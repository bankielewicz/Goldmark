# Goldmark application image (US-01). Three stages:
#   web-build  builds the browser-only SPA. It has no secrets and no publish
#              credentials; only its static output (dist/) leaves the stage.
#   api-build  restores NuGet packages in locked mode and publishes the API.
#   runtime    chiseled ASP.NET Core image (no shell, no package manager),
#              non-root, containing the published API plus the SPA's static files.
#              The "-extra" variant adds ICU, which Microsoft.Data.SqlClient
#              requires (it refuses to run in globalization-invariant mode).
# All base images are pinned by digest (DEC-013). Dependabot proposes updates.

ARG NODE_IMAGE=node:24.21.0-trixie-slim@sha256:173f125896c3b47ddf056734c7ea789d04595a6a08769a8f78e0df642781fb66
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra@sha256:3ed6558056b54d5c9dfefb7ff473728e022ff2bf1d976cbf8ee07b05ec594374

# ---------------------------------------------------------------------------
FROM ${NODE_IMAGE} AS web-build
ENV CI=true \
    COREPACK_ENABLE_DOWNLOAD_PROMPT=0
WORKDIR /src/web
# Policy files first: the dependency policy check runs before anything is installed.
COPY web/package.json web/pnpm-lock.yaml web/pnpm-workspace.yaml web/.npmrc web/dependency-allowlist.json ./
COPY web/scripts/check-dependency-policy.mjs web/scripts/check-installed-tree.mjs ./scripts/
RUN node scripts/check-dependency-policy.mjs
# Corepack verifies the pinned pnpm by its sha512 from package.json.
RUN corepack enable pnpm && pnpm install --frozen-lockfile
COPY web/ ./
# pnpm's own view of what it installed must pass before any installed code runs.
RUN node scripts/check-installed-tree.mjs && pnpm run build && node --test "tests/*.test.mjs"

# ---------------------------------------------------------------------------
FROM ${SDK_IMAGE} AS api-build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
WORKDIR /src
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Goldmark.Api/Goldmark.Api.csproj src/Goldmark.Api/packages.lock.json src/Goldmark.Api/
RUN dotnet restore src/Goldmark.Api/Goldmark.Api.csproj --locked-mode
COPY src/ src/
# Schema migrations are embedded in the API assembly; the migrate command applies them.
COPY db/migrations/ db/migrations/
RUN dotnet publish src/Goldmark.Api/Goldmark.Api.csproj -c Release --no-restore -o /app/publish -p:UseAppHost=false \
 && mkdir -p /layout/var/lib/goldmark/tls

# ---------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS runtime
LABEL org.opencontainers.image.title="Goldmark CMDB" \
      org.opencontainers.image.description="Self-hosted CMDB (under development; not for production use)" \
      org.opencontainers.image.source="https://github.com/bankielewicz/Goldmark"
# Kestrel is configured in code to listen on HTTPS only; disable the image's default HTTP port.
ENV ASPNETCORE_HTTP_PORTS= \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
COPY --from=api-build /app/publish ./
COPY --from=web-build /src/web/dist ./wwwroot/
# Mount point for the per-installation TLS certificate volume, owned by the app user.
COPY --from=api-build --chown=1654:1654 --chmod=700 /layout/var/lib/goldmark/tls /var/lib/goldmark/tls
USER 1654
EXPOSE 8443
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD ["dotnet", "/app/Goldmark.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "/app/Goldmark.Api.dll"]
