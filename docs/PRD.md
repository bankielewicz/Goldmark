# Goldmark CMDB: Product Requirements Document

**Status:** DRAFT v0.2, for review (not approved)
**Working name:** Goldmark (codename; final name pending trademark/GitHub/domain check, see OQ-1)
**Repository:** `Goldmark-cmdb` (public GitHub, Apache-2.0)
**Audience:** a coding agent that scaffolds and builds the MVP, plus human reviewers.

> **Repository note (not part of the PRD text):** this file is stored verbatim as received.
> Only the scope recorded in [`DECISIONS.md`](DECISIONS.md) is approved. Where a decision
> there narrows or deviates from this document, `DECISIONS.md` wins.

### Decision log

| ID | Decision | Rationale | Recorded as |
|---|---|---|---|
| D1 | **Frontend: React 19 + TypeScript, built with Vite as a browser-only single-page app** served as static files by ASP.NET Core. **No Next.js, no server-side rendering, no React Server Components; no Node.js at runtime.** | React has the largest ecosystem and contributor pool, the best graph-visualization libraries, and suits the dashboard-heavy add-ons planned later. Browser-only rendering avoids React's server-side vulnerability class (e.g. CVE-2025-55182). The remaining risk, the npm supply chain, is controlled by NFR-SEC-9. Blazor Server was considered (§8.2). | ADR-0001 (decided by the architect, delegated by the product owner) |
| D2 | **Add-ons integrate through platform contracts only:** API, change feed, webhooks, annotations, deep links and app registrations. They never integrate through code loaded into the CMDB UI. | Keeps proprietary add-ons independent of the CMDB's frontend framework and version, keeps the open-source/proprietary boundary clean, and keeps third-party JavaScript out of the CMDB's security context. | ADR-0002 |

### Assumptions to confirm or overturn

These items have **not** been explicitly confirmed by the product owner. They are the recommended defaults. Overturn any of them before approval.

| ID | Assumption |
|---|---|
| A1 | Backend stack: .NET 10 (LTS) / ASP.NET Core, Dapper, DbUp, SQL Server 2025 (must run on Express). The frontend is decided (D1). |
| A2 | License: Apache-2.0 for the core. Future paid apps are separate, proprietary products that use the public API (D2). Contributions use DCO sign-off (no CLA). **Revisit before accepting the first external contribution** if a commercial edition of the *core* is ever possible (OQ-10). |
| A3 | Authentication: local accounts with mandatory TOTP MFA, plus OIDC sign-in (Microsoft Entra ID first). |
| A4 | No NetBox or other IPAM/DCIM exists in the reference environment. A NetBox import is post-MVP. |
| A5 | Success metrics as listed in §2.3. |
| A6 | Self-hosted only. No SaaS in the MVP. |
| A7 | Horizon VDI session hosts are permanently out of scope. |

---

## 1. Overview

### 1.1 Problem

IT teams cannot answer basic dependency questions quickly or reliably:

- "What breaks if I put this host, switch, storage controller or SQL Server into maintenance?"
- "Which VMs and databases still sit on the storage array we are retiring?"
- "Which Windows servers exist in AD but in no hypervisor inventory, and what are they?"
- "What did this environment look like last Tuesday?"

The answers are scattered across vCenter, Active Directory, SQL Server, storage consoles, spreadsheets and people's heads.

The existing products don't fit:
- **Commercial CMDBs** (ServiceNow, BMC, Device42) are expensive and heavy to implement.
- **Open-source options** are either ITSM suites with a CMDB bolted on (GLPI, iTop), where relationships are maintained manually, or network-focused tools (NetBox).
- **Small shops** have nothing beyond spreadsheets.

### 1.2 Solution

Goldmark is an open-source, self-hosted CMDB with four distinguishing traits:

1. **Accurate automatically.** Read-only collectors push data from the systems that already know the truth (vCenter, Windows/CIM, AD, SQL Server). A deterministic **reconciliation engine** merges it using documented, tested identity rules. Every rule was validated against a real enterprise estate (see §1.4).
2. **Built around relationships and impact.** Typed relationships with **redundancy groups** produce credible "down" and "degraded" impact results.
3. **Point-in-time history.** Every CI and relationship is system-versioned, so "show me the estate as of a given date" and "who changed what" are first-class features.
4. **A platform for other apps.** A versioned REST API, stable CI UUIDs and change events (webhooks) let other tools treat Goldmark as their source of truth. The first of these will be a future endpoint-monitoring app.

It is **not** a service desk. It integrates with ticketing; it does not replace it.

### 1.3 Target users

- **Primary for the MVP:** internal IT teams of mid-size to large organizations, roughly 50–5,000 servers. They are Windows-heavy, virtualized on VMware, with SQL Server and FC SAN storage.
- **Also served by the MVP:** small shops (free, simple install, CSV import, SQL Server Express).
- **Post-MVP:** MSPs (multi-tenant UI) and very large enterprises (HA, advanced RBAC). The schema is tenant-aware from day one.

### 1.4 Reference environment (measured)

These facts come from a read-only readiness probe run against the design-partner estate. They are design inputs, not assumptions.

| Area | Measured fact | Design consequence |
|---|---|---|
| Virtualization | 2 vCenters (8.0.3), 10 clusters, 76 physical ESXi hosts (45 Dell MX740c blades, 20 Cisco UCS B200 M5 blades, 6 R740, 5 Cisco BE7M), 3 "VMware Mobility Platform" pseudo-hosts (HCX), 2,178 VMs (1,986 Windows) | Generic hypervisor model. Chassis/blade/interconnect modelling. HCX agents are typed as appliances, not hosts. |
| VM identity | 3 instanceUuid collisions across vCenters, each with exactly one powered-on member | VM identity = (vCenter instance UUID, VM instanceUuid). |
| VM-to-OS link | 38 of 38 sampled guests matched vCenter BIOS UUID **only after byte-swapping** the first three UUID fields. Hostname agreed 100%. | Endianness normalization is mandatory. |
| Windows collection | 97.5% CIM success over WinRM; 1 guest DCOM-only; 1 access denied | WinRM first, DCOM fallback. |
| Active Directory | 1,633 active server objects after excluding 856 out-of-scope objects (2 OUs). 213 failover-cluster **nodes**, 114 cluster name objects. 195 non-cluster servers not in vCenter. Sample: ~64% physical (mostly Dell PowerEdge), ~20% Xen HVM, ~15% VMware VMs in an unprobed vCenter. | AD connector is P0. Cluster objects are modelled separately. A physical/other platform classification is needed. |
| Legacy OS | 12× Server 2003, 3× 2008, 6× 2008 R2 still active | In-guest collection not supported for 2003/2008. 2008 R2 works over WinRM. |
| SQL Server | 15 instances found on sampled servers, 14 probed. Versions 10.x–15.x. 10 Enterprise, 3 Express, 1 Standard. **7 of 14 use Availability Groups**; 0 FCIs. ~16 user DBs per instance. Default (untrusted) TLS certificates on all instances. | AG modelling is P0. Must support SQL Server 2008+. Per-instance certificate trust. |
| Ethernet topology | Only **24.8%** of live uplinks see a real switch via CDP. 140 see Cisco ISE VMs and 12 see ESXi hosts (flooded CDP). | Auto-link only switch-class neighbors (P1). Never trust host-class neighbors. |
| FC storage | 581 FC LUNs, all 32/16 Gb FC (QLogic HBAs, Cisco VIC vHBAs). Every LUN reports vendor/model "IBM 2145", yet LUNs group by target WWNN into **12 controller pairs** that cover 100% of the 493 SAN datastores. 0 single-HBA or single-controller exposures. 88 FC LUNs back no datastore (likely RDMs). | Datastore → storage-group mapping is automatic via target WWNN. Group → array naming is manual. RDM links are P1. |
| Other storage | NetApp ONTAP and PowerScale/OneFS present (AD objects) | `StorageArray.kind = nas`, entered manually in the MVP. |
| Scale | ~5,300 core CIs today. SQL databases extrapolate to several thousand more. | Design target: 25k CIs, 250k relationships (§6.2). |

---

## 2. Goals & non-goals

### 2.1 Goals (MVP)

- **G1.** One authoritative CI per real-world thing, reconciled automatically from vCenter, Windows/CIM, AD, SQL Server and CSV, with traceable provenance per attribute.
- **G2.** Impact analysis across compute, storage, SQL and business-service layers, with redundancy-aware down/degraded results.
- **G3.** Point-in-time view and full change history for every CI and relationship.
- **G4.** A stable, versioned API and webhook events suitable for downstream products.
- **G5.** Secure by default: OWASP ASVS L2 target, least-privilege collectors, and no stored collector credentials on the server.
- **G6.** Installable by a small shop with Docker Compose and SQL Server Express. Reaches a first useful graph quickly (see §2.3).

### 2.2 Non-goals (MVP)

- **Not an ITSM tool:** no incident, problem or change ticketing, approvals, workflows or SLAs.
- **No active discovery:** no network scanning, SNMP polling, agent-based discovery or config backup.
- **No monitoring:** no metrics, alerting or performance data. Telemetry never goes into the CMDB.
- **No DCIM/IPAM:** no racks, cables, power, IP prefixes or VLAN management (NetBox import is post-MVP).
- **No other platform connectors:** NSX, Hyper-V, Xen/Citrix/AWS, Proxmox, UCS Manager/Intersight, Dell OME/Redfish, IBM Storage Virtualize API, Brocade, NetApp and PowerScale are all post-MVP.
- **No multi-tenant UI, HA/clustering, SaaS hosting, mobile app or i18n.**
- **No in-guest collection** for Windows Server 2003/2008 or non-Windows OSes. Those CIs come from vCenter, AD or CSV.

### 2.3 Success metrics

| ID | Metric | Target |
|---|---|---|
| M1 | Powered-on Windows VMs (Tools running) automatically linked to their Server CI | ≥ 95% (reference sample: 100%) |
| M2 | Duplicate CIs for the same real-world SQL instance, VM or server after 3 consecutive collector runs | 0 |
| M3 | CMDB links created from non-switch CDP neighbors | 0 |
| M4 | SAN datastores mapped to a storage group automatically | ≥ 99% (reference: 100%) |
| M5 | Fresh install (Docker Compose + Express) to first impact graph from CSV sample data, by a non-DBA following the README | ≤ 30 minutes |
| M6 | API endpoints covered by an automated authorization test (deny-by-default verified) | 100% |
| M7 | Usage telemetry | Opt-in only, off by default |
| M8 | Impact query, 6 hops on a 25k-CI graph | p95 ≤ 2 s |

---

## 3. User personas

| Persona | Context | Needs |
|---|---|---|
| **Dana, Infrastructure/VMware admin** (enterprise) | Runs ~80 hosts, two vCenters, blades plus FC SAN. Plans maintenance and migrations (storage refreshes, hypervisor changes). | "What breaks if…", what's on array X, VM↔OS↔SQL chain, history of changes. |
| **Sam, DBA** | Owns hundreds of SQL databases, many in Availability Groups. | Instance/AG/replica/listener topology, which apps use which DB, which server/host/storage a DB ultimately depends on. |
| **Pat, IT generalist** (small shop) | 20–200 devices, no DBA, Windows shop. | Free, easy install, CSV import, simple inventory and ownership, backups that just work. |
| **Riley, Service owner** | Accountable for a business application. | "What does my service depend on?", owners, change history. |
| **Ingrid, Security/audit** | Compliance evidence. | "Prove what was running, where, owned by whom, on date X". Audit log. Least privilege. |
| **Integrator** (machine persona) | Scripts and future paid apps (endpoint monitoring). | Stable IDs, documented API, webhooks, scoped tokens. |

---

## 4. User stories with acceptance criteria

Priority: **P0** = required for MVP; **P1** = MVP-desirable, build after all P0s.

### 4.1 Install, access & security

**US-01 (P0) Install with Docker Compose.** As Pat, I can start Goldmark with one `docker compose up`, using bundled SQL Server Express or an existing SQL Server instance.
- `.env.example` documents every variable. No default passwords exist; startup fails if required secrets are missing.
- The app runs DbUp migrations on start using a separate migration login. The runtime login has DML-only rights.
- HTTPS is on by default (self-signed certificate generated on first start, with replacement documented).
- `/health/live` and `/health/ready` endpoints exist.

**US-02 (P0) First-run setup.** As the installer, I create the first admin account through a one-time setup page protected by a setup token printed to the container log.
- The setup page is disabled permanently after use.
- The admin must enrol TOTP before reaching any other page.

**US-03 (P0) Local sign-in with MFA.** Users sign in with username, password and TOTP.
- Passwords are hashed with ASP.NET Core Identity's default hasher (PBKDF2, current iteration count).
- Lockout after 5 failures for 15 minutes (configurable).
- Recovery codes are issued once and stored hashed.

**US-04 (P0) Roles.** An admin assigns roles: `Admin`, `Editor`, `Viewer`, `Collector` (service-only), `Auditor` (read-only plus audit log).
- Every endpoint declares a policy. Endpoints without one fail closed. Covered by an automated test enumerating all endpoints (M6).

**US-05 (P1) OIDC sign-in.** Users can sign in with Microsoft Entra ID (generic OIDC supported). Group claims map to roles.

**US-06 (P0) API tokens.** An admin creates scoped, expiring tokens (e.g. `ingest:write` for one source, `cis:read`).
- Tokens are shown once, stored hashed (SHA-256), prefixed for secret scanning (`sxt_`).
- Last-used time is visible. Tokens can be revoked.

**US-07 (P0) Audit log.** As Ingrid, I can see who changed what, when and from where, across sign-ins, CI and relationship edits, admin actions and token use.
- The log is append-only (no UPDATE/DELETE permission for the app login). Filterable. CSV export.

### 4.2 CI model

**US-10 (P0) Built-in CI types.** The system ships the CI types and relationship types in §9.3, with attribute definitions.
- An admin can add custom attributes (string, number, bool, date, enum, CI-reference) to any type without a code change.

**US-11 (P0) Manual CI CRUD.** Editors can create, edit and retire CIs and relationships in the UI and API.
- Fields owned by a collector are read-only in the UI and show their source (see FR-RC-6).

**US-12 (P0) Lifecycle state.** Every CI has `lifecycle_state` ∈ {Planned, Active, Retiring, Retired}, editable and filterable.
- Retired CIs are hidden by default but kept in history.
- The impact engine treats Retired CIs as absent.

**US-13 (P0) Ownership.** A CI can have an owning Team and/or Person (CI types `Team`, `Person`), with "owned by" filters.

**US-14 (P0) Business services.** Editors create `BusinessService` CIs and link them to the CIs they depend on (VMs, servers, SQL databases, AG listeners).

### 4.3 Ingestion & reconciliation

**US-20 (P0) Ingest API.** A collector posts a batch of observed items and relationships for its registered source.
- The batch is idempotent by `batchId`. The server validates against a JSON Schema.
- Large batches are accepted asynchronously (`202` plus status URL).

**US-21 (P0) Deterministic identity matching.** Each incoming item is matched to an existing CI using the identity rules in §5.2. It updates that CI or creates a new one.
- Re-ingesting the same batch changes nothing: no new history rows, no events (change suppression).

**US-22 (P0) Provenance & precedence.** Each attribute value records its source and last-seen time.
- When two sources disagree, the configured precedence decides. The UI shows all values and which one won.

**US-23 (P0) Review queue.** Ambiguous matches go to a review queue instead of auto-merging or auto-duplicating. Examples: two candidate CIs; an identifier claimed by two CIs; a VM↔OS link with conflicting signals.
- Editors approve, reject or merge. Every decision is audited and applied idempotently.

**US-24 (P0) Absence handling.** When a full-scope source run no longer reports a CI, its `last_seen` ages.
- After N consecutive missing runs (default 3) the CI is flagged "Not seen" and a Retiring suggestion is raised.
- Nothing is deleted automatically.

**US-25 (P0) CSV import.** As Pat, I upload a CSV, map columns to a CI type and attributes, preview a dry run (create/update/conflict counts) and commit.
- Relationship CSVs are supported (from-key, type, to-key, optional redundancy group).
- Imports go through the same reconciliation engine as collectors (source = `csv:<name>`).

### 4.4 Collectors (PowerShell 7 module, read-only)

**US-30 (P0) Collector registration.** An admin registers a collector source in the UI. The UI issues a scoped ingest token and shows the exact command to install it as a scheduled task running under a gMSA.

**US-31 (P0) vCenter collector.** Collects hypervisor managers, clusters, hosts (with hardware vendor/model and host kind), VMs, datastores and relationships (VM→host, VM→datastore, host→cluster).
- VM identity rule and HCX/virtual-host typing per §5.2.
- Duplicate VMs across vCenters create separate CIs, and the review queue is not spammed by them.

**US-32 (P0) Windows collector.** Collects OS instance details over CIM: hostname, domain, OS, SMBIOS UUID, serial, manufacturer/model, platform classification, SQL services. It uses WinRM, falling back to DCOM.
- Links each Server CI to its VM using the byte-swap-aware UUID rule (§5.2).
- Records failures per target with an error class.
- Targets running Windows Server 2003/2008 are skipped and marked "in-guest collection unsupported".

**US-33 (P0) Active Directory collector.** Collects computer objects (any OS) with OU-based exclusions configured per source.
- Classifies each object as CNO, VCO (clustered network name) or cluster Node using the rule in §5.2 and stores the raw SPN flags.
- Marks stale and disabled objects.
- Produces the "in AD but not in any hypervisor" and "in AD but DNS does not resolve" data-quality lists.

**US-34 (P0) SQL Server collector.** Collects instances, databases, Availability Groups, replicas and listeners. Supports SQL Server 2008 (10.x) and later; AG features where available (2012+).
- Connections are encrypted. Certificate trust is per-instance: a trusted CA certificate or a **pinned thumbprint** recorded in collector config. `TrustServerCertificate=true` is **never** the default and is flagged in the UI if enabled.
- Runs with a least-privilege login. The documented permission set must be validated in CI against a SQL Server container.

**US-35 (P0) FC storage mapping.** The vCenter collector reports per-host FC LUN paths: NAA ID, path state, HBA, target WWNN/WWPN.
- The server derives **storage groups** (connected components of target WWNNs) and links Datastore → StorageGroup automatically.
- An editor maps each StorageGroup to a named StorageArray in the UI. The mapping persists and is re-applied on later runs.

**US-36 (P1) Ethernet neighbor links.** The vCenter collector reports CDP/LLDP neighbors per pNIC. The server creates links only from **switch-class** neighbors (§5.2), creating placeholder NetworkDevice CIs where needed.
- Host-, hypervisor- and unknown-class neighbors are stored as raw observations but never create links.

**US-37 (P1) FC HBA inventory & RDMs.** Host HBA ports (WWPN, speed, state) are stored as host components. RDM LUNs are linked to the VMs that use them.

### 4.5 Relationships, impact & history

**US-40 (P0) Relationship graph view.** From any CI, I see its neighbors to depth N (1–6). The view has type filters and a table alternative.

**US-41 (P0) Impact analysis.** I select one or more CIs as "failed" and get every dependent CI marked **Down** or **Degraded**, with the path that explains each result.
- Redundancy groups are honored: e.g. an AG with 2 replicas where one instance fails → AG Degraded, not Down.
- Results export as CSV.

**US-42 (P0) Point-in-time view.** I pick a timestamp and see any CI, its attributes and its relationships as they were then. Impact analysis can run "as of" that time.

**US-43 (P0) Change history.** Each CI shows a timeline of attribute and relationship changes, with who or what made each change (user or source) and a diff view.

**US-44 (P1) Saved impact scenarios.** I save and re-run scenarios such as "SAN group S3 maintenance".

### 4.6 Integration

**US-50 (P0) Versioned REST API.** All UI functions are available via `/api/v1`, with an OpenAPI 3.1 document generated at build time.

**US-51 (P0) Webhooks.** An admin subscribes a URL to event types (§5.6). Deliveries are signed (HMAC-SHA256), retried with backoff and visible in a delivery log.

**US-52 (P1) Event replay.** An admin can replay deliveries for a subscription since a given time (bounded by retention).

**US-53 (P0) Stable deep links.** An add-on or external tool can link to `/ci/{id}`, `/ci/{id}?tab={tab}`, `/impact?ci={id}[&ci=…]` and `/search?q=…`. These URLs are a documented, versioned contract (`docs/integration/deep-links.md`).
- Merged CIs redirect from the old Id to the surviving Id (alias lookup).
- An unknown Id returns a friendly not-found page, never an error stack.

**US-54 (P0) API stability policy.** As an add-on developer, I can rely on `/api/v1` not breaking.
- v1 changes are additive only: new endpoints, new optional fields, new event types.
- Breaking changes ship only in `/api/v2`, and v1 keeps running for at least two minor releases after v2 ships.
- Deprecations are announced in the changelog and signalled with `Deprecation`/`Sunset` response headers.
- Contract tests validate every response against the published OpenAPI document and `spec/*.schema.json` in CI.

**US-55 (P1) External annotations.** A registered app writes small status values onto a CI under its own namespace (e.g. `monitoring.status = "critical"`, `monitoring.lastSeen`, `monitoring.url`).
- The CI detail page and graph show them as badges and links.
- Annotations are **not** written to temporal history (only the current value plus `UpdatedAt` are kept).
- An app can only write its own namespace. Each value is size-limited (≤ 4 KB; ≤ 20 keys per app per CI).
- Annotations never change CI identity, attributes or lifecycle.

**US-56 (P1) Change feed.** An integrator can sync reliably by pulling `GET /api/v1/changes?cursor={c}&limit={n}`. It returns ordered change records (the same payloads as events) and a next cursor.
- Cursors stay valid for the outbox retention period. An expired cursor returns `410 Gone` with guidance to resync in full.
- Works alongside webhooks: webhooks for low latency, the change feed for completeness after downtime.

**US-57 (P1) App registrations.** An admin registers an integration app with a name, publisher, allowed token scopes, an annotation namespace, and optional "Open in {app}" link templates (e.g. `https://monitor.example/host/{ciId}`) shown on the CI detail page for chosen CI types.
- Disabling the app revokes its tokens and hides its links and badges, without deleting annotations until purged.

### 4.7 Operations

**US-60 (P0) Backups documented.** The README ships a tested PowerShell/sqlcmd backup script and a restore procedure, because Express has no SQL Agent.

**US-61 (P1) Built-in backup job.** Scheduled `BACKUP DATABASE` to a configured path using a separate backup-only login, with retention and an alert banner on failure.

**US-62 (P0) Collector health.** The dashboard shows each source's last run, item counts, errors by class and staleness.

---

## 5. Functional requirements

### 5.1 Core model

- **FR-CM-1.** Every CI has an immutable `Id` (UUID v7), `TenantId`, `CiTypeId`, `Name`, `LifecycleState`, core columns per type, and an extensible `Attributes` column using the SQL Server 2025 native `json` type.
- **FR-CM-2.** Frequently filtered attributes can be "promoted": a computed column plus an index, created by migration. Promotions are defined in type metadata.
- **FR-CM-3.** **Components** are child rows of a CI, not CIs, to avoid graph explosion: NICs, HBA ports, LUN paths/extents, disks. Each has `(CiId, Kind, Key, Attributes json)`.
- **FR-CM-4.** **Identifiers** are stored in a separate `CiIdentifier(Scheme, NormalizedValue, Scope)` table, unique per `(TenantId, Scheme, Scope, NormalizedValue)` among active claims. This table is the matching index.
- **FR-CM-5.** `Ci`, `Relationship`, `CiIdentifier` and `CiComponent` are system-versioned temporal tables. The acting principal (user or source) is captured via `SESSION_CONTEXT` into a `ChangedBy` column on every write.
- **FR-CM-6.** Relationships are typed, directed, and carry an optional `RedundancyGroup` (string) and `Source`. Relationship types declare an `ImpactPropagation` (§5.5).

### 5.2 Identity & reconciliation rules (normative)

All normalization functions live in one shared specification with **shared test vectors** (`spec/identity-vectors.json`). They are implemented identically in C# (server) and PowerShell (collectors) and tested in both test suites.

| Rule | Specification |
|---|---|
| **ID-UUID** | Normalize to 32 lowercase hex. All-zero / all-F UUIDs are invalid. |
| **ID-UUID-SWAP** | Byte-swapped form = reverse bytes of fields 1–3 (4-2-2). Matching tries direct, then swapped, then VMware serial (`VMware-xx xx…`) direct/swapped. |
| **ID-VM** | VM identity = `(vcenter.instanceUuid, vm.instanceUuid)`. BIOS UUID is **never** a VM identity, only a VM↔OS link key. |
| **ID-VMOS** | Link Server→VM when the UUID matches by ID-UUID-SWAP **and** at most one candidate VM is powered on. Tie-break: powered-on VM whose Tools hostname matches the OS short name. Anything else → review queue. |
| **ID-HOST** | Hosts with hardware vendor `VMware` are not physical: model `VMware Mobility Platform` → `Appliance(kind=hcx-mobility-agent)`; others → `HypervisorHost(virtual=true)`. Excluded from hardware, NIC and FC topology. |
| **ID-SERVER** | Server (OS instance) identity, in precedence order: AD objectGUID → SMBIOS UUID (normalized) → serial + manufacturer → FQDN. Hostname alone never auto-merges two CIs that hold conflicting stronger identifiers. |
| **ID-AD-CLUSTER** | A computer object is a cluster **network name** if (MSClusterVirtualServer/ SPN **or** description = "Failover cluster virtual network name account") **and** it has no WSMAN/ or TERMSRV/ SPN. It's a **CNO** if it also has an MSServerClusterMgmtAPI/ SPN, otherwise a **VCO**. An object with cluster SPNs plus OS-service SPNs is a cluster **Node**. Validated: 0 network-name objects matched VMs; 205 of 213 nodes matched VMs. |
| **ID-SQL** | SqlInstance identity = `(server CI, instance name)`, where the server is the host or, for clustered instances, the network-name CI. AG identity = `group_id` (GUID). Database identity = `(instance CI, database_guid)` with fallback `(instance CI, name)`; behavior across restore is an open question (OQ-6). |
| **ID-FC** | WWNs are normalized to 16 lowercase hex. LUN identity = NAA canonical name. **StorageGroup** = connected component of target WWNNs observed across all hosts for all LUNs. A group's identity = its sorted WWNN set; a stable group CI keeps its Id when its WWNN set grows. |
| **ID-MAC** | 12 lowercase hex. |
| **ID-CDP** | A neighbor is **switch-class** only if its CDP capabilities include NetworkSwitch, Router, TransparentBridge or SourceRouteBridge **and** its platform does not start with `VMware`. Host-class (e.g. ISE VMs), hypervisor-class and unknown neighbors never create relationships. |

Reconciliation behavior:

- **FR-RC-1.** Change suppression: a CI or relationship write happens only if the normalized content hash differs from the current row.
- **FR-RC-2.** Each source declares a **scope** per batch (e.g. "vCenter X, full inventory"). Absence is evaluated only within a declared full scope.
- **FR-RC-3.** Attribute precedence is configurable per CI type and attribute. Default: Manual > SQL collector > Windows collector > vCenter > AD > CSV. Manual edits to collector-owned attributes require an explicit "override" with a reason, which is shown on the CI.
- **FR-RC-4.** Conflicts that a rule cannot resolve go to the review queue with the evidence (candidate CIs, identifiers, sources).
- **FR-RC-5.** Merging CIs keeps the surviving Id, re-points relationships and identifiers, records the merged-away Id as an alias, and emits `ci.merged`.
- **FR-RC-6.** The UI shows provenance for every attribute (source, last seen, overridden yes/no).

### 5.3 Ingest contract

- **FR-IN-1.** `POST /api/v1/ingest/batches` with `{batchId, sourceId, collector:{name,version}, scope:{kind,key,full:bool}, observedAt, items[], relationships[], components[]}`.
  - Items carry `{ref, ciType, identifiers[{scheme,value,scope?}], attributes{}}`. `ref` is a batch-local key used by relationships and components.
  - The JSON Schema lives in `spec/ingest.schema.json` and is versioned.
- **FR-IN-2.** Max request size 25 MB (configurable). Larger inventories are chunked with the same `batchId` and a `part`/`parts` header. The scope is evaluated when the last part arrives.
- **FR-IN-3.** Processing is asynchronous: `202 Accepted` + `GET /api/v1/ingest/batches/{id}` returning status, counts (created/updated/unchanged/review) and errors.
- **FR-IN-4.** A token may only ingest for its own `sourceId` (enforced server-side).

### 5.4 Collectors

- **FR-CO-1.** Collectors ship as a PowerShell 7 module (`Goldmark.Collectors`) signed with the project code-signing certificate. Each collector is read-only against its target.
- **FR-CO-2.** Collectors are configured by a local JSON file. The Goldmark token is stored DPAPI-protected (CurrentUser/gMSA). The server stores **no** credentials for vCenter, Windows, AD or SQL.
- **FR-CO-3.** vCenter collector: uses VCF.PowerCLI. Sets `DefaultVIServerMode=Multiple` at session scope after a feature check. Passes `-Server` explicitly on every call. Uses StrictMode-safe property access. Does a per-NIC `QueryNetworkHint` retry when the bulk call fails.
- **FR-CO-4.** Windows collector: WinRM, with optional DCOM fallback per target. Concurrency limit (default 16). Per-target timeout. Error classes as in the readiness probe.
- **FR-CO-5.** SQL collector: `Microsoft.Data.SqlClient`, encryption required, per-instance trust config (CA or pinned thumbprint). Queries are version-gated (no AG DMVs below 11.x). SELECT-only.
- **FR-CO-6.** AD collector: LDAP paged search over computer objects (no OS filter, so cluster network names are included). OU exclusion list. Attributes: name, dNSHostName, OS, lastLogonTimestamp, userAccountControl, servicePrincipalName, description, objectGUID.
- **FR-CO-7.** Every collector emits a run summary (counts, error classes, duration) to the ingest API, shown on the dashboard.

### 5.5 Impact engine

- **FR-IM-1.** Each relationship type declares `ImpactPropagation` ∈ {`None`, `Down`}. Direction is always dependent → dependency: "A depends on B" means a failure of B impacts A.
- **FR-IM-2.** Evaluation: starting from the failed set, walk dependents breadth-first, depth-limited (default 10, max 20), with cycle detection.
  - A dependent is **Down** if any non-redundant `Down` dependency is Down, or if **all** members of any redundancy group it depends on are Down.
  - It is **Degraded** if some, but not all, members of a redundancy group are Down/Degraded, or if a non-redundant dependency is Degraded.
- **FR-IM-3.** The subgraph is fetched with a recursive CTE (or iterative batched queries) and evaluated in C#. Results include explanation paths.
- **FR-IM-4.** Retired CIs and relationships ending before the as-of time are ignored. The `asOf` parameter applies to all reads.

### 5.6 Events & webhooks

- **FR-EV-1.** Event types:
  - `ci.created`, `ci.updated` (with changed attribute names), `ci.retired`, `ci.merged`
  - `relationship.created`, `relationship.removed`
  - `review.created`, `source.run.completed`
- **FR-EV-2.** Transactional outbox: the event row is written in the same transaction as the change. A background dispatcher delivers at least once, ordered per CI.
- **FR-EV-3.** Signature header `X-Goldmark-Signature: t=<unix>,v1=<hex HMAC-SHA256(secret, t.body)>`. Receivers are documented to reject skew greater than 5 minutes.
- **FR-EV-4.** Retries use exponential backoff up to 24 h, then dead-letter. The delivery log is retained for 30 days (configurable).
- **FR-EV-5.** The change feed (US-56) reads the same outbox, ordered by its monotonically increasing `bigint` Id. The cursor is an opaque, signed encoding of that Id. Outbox retention defaults to 30 days, so the change feed and webhook replay share one retention setting.

### 5.6a Extension points for add-ons (D2)

- **FR-EX-1.** Add-ons integrate **only** through:
  - the REST API and change feed;
  - webhooks;
  - the ingest API (an add-on such as a monitoring agent may register as an ingest source);
  - external annotations;
  - deep links;
  - app-registered link templates.

  The CMDB UI loads **no third-party code**: no plugins, module federation or remote scripts.
- **FR-EX-2.** `Goldmark.Contracts` (DTOs, ingest/event schemas) is published as a NuGet package under Apache-2.0, and a generated TypeScript types package is published to npm. Both are versioned in lockstep with the API.
- **FR-EX-3.** Annotations live in `CiAnnotation`, which is **not** system-versioned. Writes are last-write-wins per `(CiId, Namespace, Key)` and emit `ci.annotation.changed` only when a value changes (a value-hash check), so monitoring churn doesn't flood the outbox.
- **FR-EX-4.** Reserved for post-MVP: embedded add-on tabs (an iframe with a `postMessage` contract and per-app `frame-src` allow-listing). URL and message schema placeholders are documented in `docs/integration/embedding.md` but not implemented.
- **FR-EX-5.** Authentication design constraint: user sign-in, API tokens and app registrations sit behind an internal abstraction. That way the CMDB can later act as an OIDC provider for add-ons in small shops without an external identity provider (post-MVP) without reworking authorization.

### 5.7 API

`/api/v1/` resources:
- `cis` (search/filter/paging, `asOf`)
- `cis/{id}`
- `cis/{id}/relationships`
- `cis/{id}/history`
- `cis/{id}/components`
- `relationships`
- `types` (CI types, attributes, relationship types)
- `impact` (POST)
- `ingest/batches`
- `imports` (CSV)
- `review-items`
- `storage-groups` (mapping)
- `webhooks`
- `tokens`
- `users`
- `audit`
- `sources`
- `health`

API conventions:
- Cursor-based paging, filtering on core and promoted attributes, `ETag`/`If-Match` optimistic concurrency on writes.
- Problem Details (RFC 9457) errors.
- Rate limiting per token.

---

## 6. Non-functional requirements

### 6.1 Security

- **NFR-SEC-1.** Target **OWASP ASVS v5 Level 2**, checked with a checklist in `docs/security/asvs.md`. A threat model (STRIDE) for the ingest path and auth lives in `docs/security/threat-model.md`.
- **NFR-SEC-2.** Deny-by-default authorization (M6). Anti-forgery on cookie-authenticated mutations. HSTS. `SameSite=Lax` (or stricter) cookies. Strict CSP: `default-src 'self'`; no `unsafe-inline` or `unsafe-eval`; `frame-ancestors 'none'` by default; **Trusted Types enforced** (`require-trusted-types-for 'script'`).
- **NFR-SEC-3.** SQL injection: every Dapper call is parameterized. Dynamic filters/sorts use identifiers from type metadata through an allow-list. A Semgrep (or Roslyn analyzer) rule fails the build on string interpolation inside Dapper calls.
- **NFR-SEC-4.** Least-privilege database:
  - The runtime login has DML on app tables, no DDL, and no UPDATE/DELETE on `AuditLog`.
  - A separate migration login.
  - A separate backup login (P1).
  - SQL connections encrypted. TDS 8 strict is supported.
- **NFR-SEC-5.** Secrets (OIDC client secret, webhook secrets) are encrypted at rest with ASP.NET Core Data Protection; keys are persisted to a protected volume. API tokens and recovery codes are stored hashed.
- **NFR-SEC-6.** The CMDB is a **sensitive asset** (a map of the estate). Documentation requires disk encryption on the DB host (Express has no TDE). Pseudonymized export is a P1 feature that reuses the probe's keyed-HMAC design.
- **NFR-SEC-7.** Supply chain:
  - CodeQL, Dependabot and dependency review on every PR.
  - SBOM (CycloneDX) per release.
  - Container images signed (cosign) and pinned by digest.
  - Collector module code-signed.
  - `SECURITY.md` with a disclosure process.
- **NFR-SEC-8.** Collector safety: read-only operations only. Documentation lists the exact least-privilege role/permissions per collector and warns about EDR/SIEM noise.
- **NFR-SEC-9. Frontend supply chain (D1).**
  - **Browser-only build:** no Node.js at runtime; no Next.js, SSR or React Server Components (CI check: none of `next`, `react-server-dom-*` or `@remix-run/*` may appear in the lockfile).
  - **Dependency allow-list:** direct dependencies are listed in `web/dependency-allowlist.json`, each with a justification. Adding one requires a reviewed PR (enforced in CI). Initial runtime allow-list: `react`, `react-dom`, `@fluentui/react-components`, `@fluentui/react-icons`, `cytoscape`, `react-router`. Data fetching uses `fetch` plus small in-repo hooks; no data-fetching framework.
  - **Reproducible, script-free installs:** pnpm with a committed lockfile and `--frozen-lockfile`; lifecycle scripts disabled (`ignore-scripts`, with explicit allow-listing only if ever required); a minimum release-age delay before newly published versions can be adopted; Dependabot with a cooldown.
  - **Isolated build:** the frontend builds in a CI job with **no secrets and no publish credentials**. Artifacts pass to the release job by digest.
  - **Code rules:** ESLint bans `dangerouslySetInnerHTML`, `eval` and `new Function`. Any HTML rendering goes through one reviewed Trusted Types policy.
  - **Transparency:** the SBOM covers both the NuGet and npm trees; npm dependency review on every PR.

### 6.2 Performance & scale (reference: SQL Server Express limits of 1 socket/4 cores, ~1.4 GB buffer pool, 50 GB per DB)

| ID | Target |
|---|---|
| NFR-PERF-1 | Scale: 25k CIs, 250k relationships, 500k components, 5 years of history on Express within 50 GB at typical change rates. Document the growth estimate method. |
| NFR-PERF-2 | CI search/list p95 ≤ 500 ms; CI detail p95 ≤ 300 ms (25k CIs). |
| NFR-PERF-3 | Impact (6 hops, 25k-CI graph) p95 ≤ 2 s (M8). |
| NFR-PERF-4 | Ingest: a 10k-item full-scope batch with ≤ 1% changes processes in ≤ 2 min on Express. |
| NFR-PERF-5 | A performance test suite (synthetic generator reproducing the reference shape in §1.4) runs in CI on demand. |

### 6.3 Accessibility

- **NFR-A11Y-1.** WCAG 2.2 AA for all screens. Keyboard-only operation for all functions. Visible focus. Color is never the only status signal (Down/Degraded use icon plus text).
- **NFR-A11Y-2.** Every graph view has an equivalent accessible table view.
- **NFR-A11Y-3.** Automated axe checks in the Playwright end-to-end suite.

### 6.4 Logging & observability

- **NFR-LOG-1.** Structured JSON logs to stdout (Serilog) with a correlation ID per request and batch. No secrets, tokens or passwords in logs. Hostnames appear only at Debug level.
- **NFR-LOG-2.** OpenTelemetry traces and metrics, with an exporter that is disabled unless configured. Key metrics: ingest latency/queue depth, review queue size, webhook failures, impact query time.
- **NFR-LOG-3.** The audit log (US-07) is separate from operational logs and retained for ≥ 1 year (configurable).

### 6.5 Reliability & compatibility

- **NFR-REL-1.** Supported database: SQL Server 2025 (Express, Standard, Enterprise; Linux container or Windows). Requires the native `json` type (see OQ-2).
- **NFR-REL-2.** Containers are x64 only (SQL Server on Linux has no ARM64 build).
- **NFR-REL-3.** Collector host: Windows with PowerShell 7.4+ and VCF.PowerCLI 9.x. Targets: Windows Server 2008 R2+ (in-guest) and SQL Server 2008+.
- **NFR-REL-4.** Browsers: current Edge, Chrome and Firefox.

---

## 7. UI inventory

Each screen below will get a mockup in Claude Design.

| # | Screen | Purpose | Key elements |
|---|---|---|---|
| 1 | **Sign-in** | Authenticate | Username/password, TOTP step, "Sign in with Microsoft" (P1), lockout message |
| 2 | **First-run setup** | Create first admin, enrol MFA | Setup-token field, admin form, TOTP QR + verification, recovery codes download |
| 3 | **Dashboard** | Estate health at a glance | CI counts by type, collector health tiles (last run, errors, staleness), review queue count, data-quality counts, recent changes feed |
| 4 | **CI search / list** | Find CIs | Global search, type facet, lifecycle/owner/source filters, saved filters, column chooser, CSV export, "as of" date picker |
| 5 | **CI detail** | Everything about one CI | Header (name, type, lifecycle, owner, **external status badges from apps (P1)**, **"Open in {app}" links (P1)**); tabs: Attributes (with provenance badges and override control), Relationships (grouped by type), Components, History (timeline + diff), Identifiers, Impact (quick "what depends on this"). The URL is a stable deep link (US-53). |
| 6 | **Graph explorer** | Visual dependency view | Cytoscape canvas, depth slider (1–6), type filters, layout toggle, "as of" control, accessible table view toggle, open CI on click |
| 7 | **Impact analysis** | What-if failure | Pick failed CIs (search + multi-select), run; result list with Down/Degraded status, explanation path per row, graph overlay, CSV export, save scenario (P1) |
| 8 | **Business service editor** | Define services and dependencies | Service form, dependency picker, owner, criticality, quick impact preview |
| 9 | **Review queue** | Resolve reconciliation conflicts | Item list by kind, evidence panel (candidates, identifiers, sources), actions: merge/link/reject/create, bulk actions, audit trail |
| 10 | **Storage group mapping** | Name FC storage groups | Groups S1…Sn with WWNN sets, LUN/datastore/host counts, assign to StorageArray (create or pick), confirm |
| 11 | **Data quality** | Hygiene reports | AD-not-in-hypervisor, AD-not-in-DNS, VMs without OS link, stale/not-seen CIs, CDP non-switch neighbor summary, duplicate suspects; each list exportable |
| 12 | **Sources & collectors** | Manage ingest sources | Register source, issue token, install command, run history, per-run counts/errors, scope, absence threshold, precedence settings |
| 13 | **CSV import wizard** | Bulk import | Upload, choose CI type or relationship mode, column mapping, dry-run result (create/update/conflict), commit, import history |
| 14 | **Types & attributes admin** | Extend the model | CI type list, attribute definitions (type, required, enum values), promoted-attribute flag, relationship types with impact propagation |
| 15 | **Change history (global)** | Cross-estate change log | Filter by CI type, source, user, time range; diff view; export |
| 16 | **Audit log** | Security/audit trail | Filter by actor, action, target, time; export; read-only |
| 17 | **Users & roles** | Access management | User list, role assignment, MFA status, lock/unlock, OIDC group mappings (P1) |
| 18 | **API tokens** | Integration access | Create (scopes, expiry, source binding), shown-once secret, last used, revoke |
| 19 | **Webhooks** | Event subscriptions | Endpoint, event types, secret rotate, test delivery, delivery log with retry |
| 20 | **Settings** | System configuration | Instance name, TLS info, OIDC (P1), retention (history, audit, deliveries/change feed), backup settings (P1), telemetry opt-in (off by default) |
| 21 | **Apps & integrations** (P1) | Register add-ons | App list (name, publisher, status); register/edit: allowed scopes, annotation namespace, link templates per CI type; issue/revoke app tokens; last activity; disable app |

---

## 8. Tech stack

### 8.1 Recommendation

| Layer | Choice | Rationale |
|---|---|---|
| Runtime / API | **.NET 10 LTS, ASP.NET Core** (minimal APIs + endpoint filters) | Mature built-in security (Identity, Data Protection, antiforgery, authorization policies). LTS until Nov 2028. Matches the owner's ability to review the code. |
| Data access | **Dapper** + SQL in `.sql` embedded resources | Explicit, tunable SQL for a DBA owner. No ORM magic in hot paths. |
| Migrations | **DbUp** (versioned, forward-only scripts) | Plain T-SQL scripts reviewable by a DBA. |
| Database | **SQL Server 2025** (Express-compatible) | Temporal tables for point-in-time; native `json` type; free Express up to 50 GB; owner expertise; enterprise familiarity. |
| Background work | `BackgroundService` hosted in the API process + SQL-backed job/outbox tables | Nothing extra to operate for small shops. |
| Auth | ASP.NET Core Identity (local + TOTP), OpenID Connect handler (Entra, P1), custom hashed PATs | Built-in, well-audited components. |
| Frontend | **React 19 + TypeScript + Vite** (browser-only SPA, D1), **Fluent UI React v9**, **Cytoscape.js**, React Router; `fetch` + in-repo hooks; pnpm | Largest ecosystem and contributor pool; accessible Microsoft-maintained component library; strong graph visualization; deliberately small dependency set (NFR-SEC-9). |
| Collectors | **PowerShell 7 module** (VCF.PowerCLI, CIM, Microsoft.Data.SqlClient, System.DirectoryServices) | Runs where credentials live. Owner expertise. Validated by the readiness probe. |
| Testing | xUnit, **Testcontainers** (SQL Server 2025 image), Pester 5, Playwright + axe | Real-database integration tests; shared identity vectors tested in both languages. |
| Packaging | Docker images (app), Docker Compose (app + SQL Express), signed releases | One-command install. |

### 8.2 Alternative considered

**Python (Django + DRF) + PostgreSQL**, the NetBox-style approach.
- **Pros:** larger open-source contributor pool, no SQL Server licensing questions, ARM support.
- **Cons:** no system-versioned temporal tables (point-in-time would need trigger-based history); no native Dapper-style SQL control; weaker fit with the owner's SQL Server/PowerShell expertise and the Windows-centric target estate.
- **Rejected for the MVP.** The repository layer keeps all SQL behind interfaces so a PostgreSQL provider remains *possible* later; this is not promised.

**Frontend alternative: Blazor Server (ASP.NET Core).**
- **Pros:** C# end to end; mostly Microsoft-maintained NuGet dependencies, so far less npm exposure; UI logic stays on the server.
- **Cons:** a stateful server connection per user (memory, reconnect handling) and a server round trip per interaction; the graph explorer would still need a JavaScript library via JS interop; a much smaller contributor and ecosystem pool (~7% vs ~45% developer usage, Stack Overflow 2025); weaker fit for the dashboard-heavy add-ons planned later.
- **Rejected (D1).** React with NFR-SEC-9's supply-chain controls is preferred for an open-source product that needs adoption.

---

## 9. Architecture & data model sketch

### 9.1 Components

```
 ┌──────────────────────── Collector host (Windows, gMSA) ────────────────────────┐
 │  Goldmark.Collectors (PS7): vCenter | Windows(CIM) | AD(LDAP) | SQL | CSV        │
 │  creds stay here (gMSA / DPAPI); token = scoped ingest:write                    │
 └───────────────┬─────────────────────────────────────────────────────────────────┘
                 │ HTTPS  POST /api/v1/ingest/batches   (JSON Schema validated)
 ┌───────────────▼──────────────────────── Goldmark server (container) ─────────────┐
 │  ASP.NET Core API ── AuthN/Z (Identity, TOTP, OIDC, PAT) ── OpenAPI             │
 │     │                                                                             │
 │     ├─ Ingest queue (SQL table) ─► Reconciliation engine (identity rules §5.2,   │
 │     │                               precedence, change suppression, review queue)│
 │     ├─ Impact engine (CTE fetch + C# evaluation, redundancy groups)              │
 │     ├─ Outbox dispatcher ─► Webhooks (HMAC)        Change feed (pull, cursor)   │
 │     ├─ Jobs: absence/staleness, storage-group derivation, retention, backup(P1)  │
 │     └─ React SPA (static files, browser-only; no Node.js at runtime)            │
 └───────────────┬───────────────────────────────────────────────────────────────────┘
                 │ TDS (encrypted)  runtime login: DML only
 ┌───────────────▼───────────────┐
 │ SQL Server 2025 (Express+)     │  temporal tables, json, audit (append-only)
 └────────────────────────────────┘

 Proprietary add-ons (e.g. endpoint monitoring), separate processes and UIs:
   read:   REST /api/v1, change feed, webhooks          write: annotations (own namespace), ingest (as a source)
   link:   deep links /ci/{id} ◄──► "Open in {app}" link templates      auth: app registration + scoped tokens
```

### 9.2 Core tables (abridged)

```
Tenant(Id, Name)                                       -- single row in MVP
CiType(Id, Name, ParentTypeId?, IsBuiltIn, Icon)
AttributeDef(Id, CiTypeId, Name, DataType, IsRequired, EnumValues json, IsPromoted, OwnerSourcePolicy)
Ci(Id uuid PK, TenantId, CiTypeId, Name, LifecycleState, OwnerTeamId?, OwnerPersonId?,
   Attributes json, ContentHash, ChangedBy, ValidFrom, ValidTo)     -- SYSTEM_VERSIONING ON
CiIdentifier(Id, TenantId, CiId, Scheme, Scope, NormalizedValue, SourceId, ChangedBy, ValidFrom, ValidTo)
   UNIQUE (TenantId, Scheme, Scope, NormalizedValue) WHERE active       -- the matching index
CiAlias(OldCiId, NewCiId, MergedAt, MergedBy)
CiComponent(Id, CiId, Kind, ComponentKey, Attributes json, ContentHash, ...)  -- temporal
AttributeProvenance(CiId, AttributeName, SourceId, Value json, LastSeenAt, IsWinning, IsOverride, OverrideReason)
RelationshipType(Id, Name, FromCiTypeId?, ToCiTypeId?, ImpactPropagation, IsBuiltIn)
Relationship(Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, RedundancyGroup?, SourceId?,
   ContentHash, ChangedBy, ValidFrom, ValidTo)                           -- temporal; From = dependent
Source(Id, Name, Kind, ScopeKey, AbsenceThreshold, Precedence, LastRunAt, ...)
IngestBatch(Id, SourceId, Status, Parts, Counts json, Errors json, ReceivedAt, CompletedAt)
SourceSighting(SourceId, CiId, ScopeKey, LastSeenAt, MissedRuns)
ReviewItem(Id, Kind, Status, Evidence json, CreatedAt, ResolvedBy, Resolution json)
StorageGroup(Id, WwnnSetHash, Wwnns json, StorageArrayCiId?)            -- derived; mapping is manual
Outbox(Id bigint, EventType, CiId?, Payload json, CreatedAt, DispatchedAt?)   -- also backs the change feed (FR-EV-5)
WebhookSubscription(Id, Url, EventTypes json, SecretProtected, IsActive, AppRegistrationId?)
WebhookDelivery(Id, SubscriptionId, OutboxId, Attempt, Status, ResponseCode, NextAttemptAt)
AppRegistration(Id, Name, Publisher, AnnotationNamespace UNIQUE, AllowedScopes json, LinkTemplates json, IsEnabled, CreatedBy)
CiAnnotation(CiId, AppRegistrationId, Namespace, Key, Value json, ValueHash, UpdatedAt)  -- NOT temporal; PK (CiId, Namespace, Key)
AuditLog(Id bigint, At, ActorType, ActorId, Action, TargetType, TargetId, Ip, Details json)  -- append-only
Users/Roles/UserRoles/UserTokens (ASP.NET Identity), ApiToken(Id, Prefix, Hash, Scopes json, SourceId?, AppRegistrationId?, ExpiresAt, LastUsedAt)
```

### 9.3 Built-in CI types (MVP)

| Group | CI types |
|---|---|
| Location & ownership | `Location`, `Team`, `Person` |
| Compute | `HypervisorManager` (vCenter), `HypervisorCluster`, `HypervisorHost` (with `virtual` flag), `Chassis`, `VirtualMachine`, `Server` (OS instance; `platform` ∈ physical, vmware, hyperv, xen, cloud, other), `Appliance` (`kind`: hcx-mobility-agent, ise, nas-head, other) |
| Network | `NetworkDevice` (`role` ∈ switch, router, firewall, load-balancer, fabric-interconnect, chassis-iom, fc-switch) |
| Storage | `Datastore`, `StorageGroup` (FC controller pair), `StorageArray` (`kind` ∈ block, nas; `family`) |
| Clustering & SQL | `WindowsCluster` (CNO), `ClusterNetworkName` (VCO / AG listener / FCI name), `SqlInstance`, `SqlAvailabilityGroup`, `SqlDatabase` |
| Business | `BusinessService` |

### 9.4 Built-in relationship types

The direction is always dependent → dependency.

| Type | From → To | Impact | Notes |
|---|---|---|---|
| `runs_on` | VirtualMachine → HypervisorHost | Down | |
| `member_of_cluster` | HypervisorHost → HypervisorCluster | None | Cluster "depends on" hosts is modelled via `cluster_capacity` (P1) |
| `os_of` | Server → VirtualMachine | Down | Created by ID-VMOS |
| `uses_datastore` | VirtualMachine → Datastore | Down | |
| `backed_by` | Datastore → StorageGroup | Down | Automatic (ID-FC) |
| `part_of_array` | StorageGroup → StorageArray | Down | Manual mapping (US-35) |
| `virtualizes` | StorageArray(SVC) → StorageArray(backend) | Down | Manual |
| `housed_in` | HypervisorHost → Chassis | Down | P1 data source; manual/CSV in MVP |
| `uplinks_to` | HypervisorHost → NetworkDevice | Down | `RedundancyGroup` = vSwitch/vDS name. P1 auto via ID-CDP |
| `hosted_on` | SqlInstance → Server | Down | |
| `database_of` | SqlDatabase → SqlInstance | Down | Non-AG databases |
| `replica_on` | SqlAvailabilityGroup → SqlInstance | Down | `RedundancyGroup` = AG id: AG Down only if all replicas Down |
| `in_ag` | SqlDatabase → SqlAvailabilityGroup | Down | AG databases |
| `listener_for` | ClusterNetworkName → SqlAvailabilityGroup | Down | |
| `cluster_node_of` | WindowsCluster → Server | Down | `RedundancyGroup` = cluster id |
| `depends_on` | BusinessService/any → any | Down | Generic manual dependency |
| `owned_by` | any → Team/Person | None | |

### 9.5 Integration architecture for add-ons (D2)

| Need of an add-on (e.g. monitoring) | Mechanism | Notes |
|---|---|---|
| Know which endpoints exist and keep a local copy | `GET /api/v1/cis` once, then the change feed (`/changes?cursor=`) | Reliable after downtime; webhooks optional for low latency |
| React to changes quickly | Webhooks (HMAC-signed) | At-least-once; consumers must be idempotent (event Id) |
| Show its status inside the CMDB | Annotations in its own namespace → badges on CI detail and graph | Not versioned, so no history bloat (FR-EX-3) |
| Send users to its UI from the CMDB | App link templates → "Open in {app}" | Framework-independent |
| Send users to the CMDB from its UI | Deep links `/ci/{id}` (US-53) | Stable, documented contract |
| Contribute inventory (e.g. an endpoint agent) | Register as an ingest source and use the ingest API | Same reconciliation rules as every other source |
| Authenticate | App registration + scoped app tokens; user SSO through the organization's IdP | CMDB-as-OIDC-provider is post-MVP (FR-EX-5) |
| Typed client code | `Goldmark.Contracts` NuGet + generated TS types (FR-EX-2) | Versioned with the API (US-54) |

Explicitly **not** supported: loading add-on code into the CMDB UI, add-ons writing core attributes or relationships directly (they must use ingest), and add-ons reading the database directly.

---

## 10. Repo structure proposal

```
Goldmark-cmdb/
├─ src/
│  ├─ Goldmark.Api/              # ASP.NET Core host: endpoints, auth, OpenAPI, static SPA hosting
│  ├─ Goldmark.Core/             # domain: types, identity rules, reconciliation, impact engine (no I/O)
│  ├─ Goldmark.Data/             # Dapper repositories, embedded .sql, SESSION_CONTEXT plumbing
│  ├─ Goldmark.Jobs/             # BackgroundServices: ingest processor, outbox, absence, storage groups
│  └─ Goldmark.Contracts/        # public DTOs + ingest/event schemas (NuGet for downstream apps)
├─ db/
│  └─ migrations/               # DbUp scripts: 0001_baseline.sql, 0002_temporal.sql, ...
├─ web/                         # React + TS + Vite SPA, browser-only (Fluent UI, Cytoscape)
│  ├─ dependency-allowlist.json # approved direct dependencies + justification (NFR-SEC-9, CI-enforced)
│  └─ .npmrc / pnpm config      # frozen lockfile, ignore-scripts, minimum release age
├─ collectors/
│  ├─ Goldmark.Collectors/       # PS7 module: Public/ Private/ (shared normalization), .psd1
│  └─ tests/                    # Pester tests, incl. spec/identity-vectors.json
├─ spec/
│  ├─ identity-vectors.json     # shared normalization/matching test vectors (C# + Pester)
│  ├─ ingest.schema.json        # versioned ingest contract
│  ├─ events.schema.json        # event + change-feed record schema
│  └─ openapi/v1.json           # generated at build; contract tests diff against the committed copy
├─ tools/
│  ├─ readiness-probe/          # Invoke-CmdbReadinessProbe.ps1 (assessment tool)
│  └─ synthetic-estate/         # generator reproducing the reference shape for perf tests
├─ tests/
│  ├─ Goldmark.Core.Tests/       # unit tests (identity rules, impact semantics)
│  ├─ Goldmark.Api.Tests/        # integration (Testcontainers SQL 2025), authz enumeration test
│  └─ e2e/                      # Playwright + axe
├─ deploy/
│  ├─ docker-compose.yml        # app + SQL Server 2025 Express
│  ├─ docker-compose.external-sql.yml
│  └─ .env.example
├─ docs/
│  ├─ adr/                      # architecture decision records
│  ├─ security/                 # asvs.md, threat-model.md, collector-permissions.md
│  ├─ integration/              # deep-links.md, api-stability.md, annotations.md, change-feed.md, embedding.md (reserved)
│  └─ ops/                      # install, backup/restore, upgrade, sizing
├─ .github/workflows/           # ci.yml (build, test, CodeQL, dependency review, SBOM), release.yml (cosign)
├─ Dockerfile
├─ LICENSE (Apache-2.0)  NOTICE  SECURITY.md  CONTRIBUTING.md (DCO)  CODE_OF_CONDUCT.md  README.md
```

---

## 11. MVP build plan

The slices are ordered, each one PR-sized, and each leaves `main` releasable. Every slice includes its own tests, docs updates and authz test entries.

| # | Slice | Pri | Done when |
|---|---|---|---|
| 1 | Repo skeleton, CI pipeline (build, test, CodeQL, dependency review), Dockerfile, Compose with SQL 2025 Express, `/health/*`, Serilog JSON; **web skeleton with NFR-SEC-9 controls** (pnpm frozen lockfile, ignore-scripts, release-age delay, dependency allow-list check, SSR/RSC package ban, secret-free frontend build job) | P0 | `docker compose up` serves health endpoints and the empty SPA; CI green; adding an unlisted npm dependency fails CI |
| 2 | DbUp baseline: Tenant, CiType, Ci (temporal), RelationshipType, Relationship (temporal), AuditLog; runtime vs migration logins | P0 | Migrations idempotent; runtime login cannot run DDL or UPDATE/DELETE on AuditLog (tested) |
| 3 | First-run setup + local auth + TOTP + roles + deny-by-default policy + authz enumeration test | P0 | US-02/03/04 acceptance criteria pass |
| 4 | Audit log writes + Audit log screen | P0 | US-07 |
| 5 | CI types/attributes metadata + manual CI CRUD API + CI list/detail UI (attributes tab) + stable deep-link routes (`/ci/{id}`, `/search?q=`) | P0 | US-10/11, US-53 (routes); search p95 target on 25k synthetic CIs |
| 6 | Relationships CRUD + relationship tab + graph explorer (depth, filters, table view) | P0 | US-40 |
| 7 | History & as-of: `SESSION_CONTEXT` ChangedBy, history tab with diff, `asOf` on reads | P0 | US-42/43 |
| 8 | API tokens (scoped, hashed, prefixed) + OpenAPI document committed to `spec/openapi/v1.json` + contract tests + `Deprecation`/`Sunset` header support + `docs/integration/api-stability.md` | P0 | US-06/50/54; CI fails on an unreviewed breaking OpenAPI diff |
| 9 | Shared identity spec: `identity-vectors.json`, C# normalization library, PS normalization module, both test suites green | P0 | All §5.2 vectors pass in both languages |
| 10 | Ingest API (schema, async batches, parts, idempotency) + CiIdentifier matching + change suppression | P0 | US-20/21; re-ingest produces 0 history rows |
| 11 | Provenance + precedence + overrides | P0 | US-22, FR-RC-3/6 |
| 12 | Review queue (API + UI) + merge/alias + deep-link redirect from merged Ids | P0 | US-23, FR-RC-5, US-53 (alias redirect) |
| 13 | Sources & collectors screen + registration + token issue + run summaries + dashboard health tiles | P0 | US-30/62 |
| 14 | vCenter collector: managers, clusters, hosts (kind), VMs (ID-VM), datastores, relationships; scope/absence | P0 | US-31/24; HCX hosts typed as Appliance |
| 15 | Windows collector (WinRM + DCOM) + ID-VMOS linking + platform classification | P0 | US-32; M1 on synthetic + reference shape |
| 16 | AD collector + cluster classification + OU exclusions + data-quality lists | P0 | US-33; ID-AD-CLUSTER vectors |
| 17 | SQL collector: instances, DBs, AGs, replicas, listeners; per-instance cert trust; version gating; least-privilege permissions validated in container test | P0 | US-34 |
| 18 | FC: LUN path components, storage-group derivation, Datastore→StorageGroup, storage-group mapping screen | P0 | US-35; M4 |
| 19 | Impact engine + impact screen + redundancy semantics + explanation paths | P0 | US-41; M8 on synthetic estate |
| 20 | Business services, ownership, lifecycle states | P0 | US-12/13/14 |
| 21 | CSV import wizard (CIs + relationships, dry run) + sample dataset for quickstart | P0 | US-25; M5 walkthrough passes |
| 22 | Outbox + webhooks + delivery log | P0 | US-51 |
| 23 | Data quality screen + global change history screen | P0 | Screens 11 and 15 |
| 24 | Release hardening: ASVS L2 checklist, threat model, SBOM, cosign, signed collector module, backup script + restore doc | P0 | US-60, NFR-SEC-7 |
| 25 | OIDC sign-in (Entra) + group→role mapping | P1 | US-05 |
| 26 | Ethernet neighbor links (ID-CDP) + NetworkDevice placeholders | P1 | US-36; M3 |
| 27 | FC HBA inventory + RDM→VM links | P1 | US-37 |
| 28 | Built-in backup job; saved impact scenarios; event replay; pseudonymized export | P1 | US-61/44/52 |
| 29 | App registrations + app tokens + annotations API (namespaced, non-versioned) + badges and "Open in {app}" links on CI detail/graph + Apps & integrations screen | P1 | US-55/57, FR-EX-3; a sample external app (test fixture) can annotate and link without any CMDB UI code change |
| 30 | Change feed (`/api/v1/changes`, signed cursor, 410 on expiry) + published `Goldmark.Contracts` NuGet and generated TypeScript types package | P1 | US-56, FR-EV-5, FR-EX-2; the sample app resyncs correctly after a simulated outage |

---

## 12. Risks, open questions, and future ideas

### 12.1 Risks

| ID | Risk | Mitigation |
|---|---|---|
| R1 | **Reconciliation errors** (duplicates or wrong merges) destroy trust. | Normative rules (§5.2) backed by shared vectors; review queue instead of guessing; M2 tracked per release; synthetic estate tests. |
| R2 | **SQL Server 2025 requirement** may conflict with corporate DB standards. | Express is free and containerized; see OQ-2. Fallback: a 2022 profile storing JSON as `nvarchar(max)` + `ISJSON` (deferred). |
| R3 | **History growth** on Express (50 GB cap, 1.4 GB buffer pool). | Change suppression; components outside hot paths; retention settings; sizing doc; perf suite. |
| R4 | **Collector credentials are highly privileged.** | Push model with no credentials on the server; gMSA; read-only roles documented; SQL least-privilege validated (the reference run used sysadmin on 12 of 14 instances, which must not carry over). |
| R5 | **Legacy targets** (SQL Server 2008 and Windows 2008 R2 TLS/WinRM quirks). | Version-gated queries; per-target error classes; explicit "unsupported" state rather than silent gaps. |
| R6 | **CDP data is misleading** (flooded frames). | ID-CDP classification; links are P1; M3 = 0 false links. |
| R7 | **Scope creep** toward ITSM/discovery/monitoring. | Non-goals in §2.2. Future products use the API rather than growing the core. |
| R8 | **Bus factor / single maintainer.** | ADRs, high test coverage on Core, DCO, contributor docs. |
| R9 | **Name/trademark collision** on public release. | OQ-1 before the first public tag. |
| R10 | **npm supply-chain compromise** (phished maintainers, self-propagating worms such as Shai-Hulud, waves hitting UI/graph libraries) reaching developer machines, CI or the shipped bundle. | D1 browser-only build; NFR-SEC-9 (allow-list, frozen lockfile, ignore-scripts, release-age delay, secret-free build job, SBOM); contributors need MFA on GitHub. |
| R11 | **Add-ons couple to CMDB internals** (UI code, database, undocumented endpoints) and break on upgrade, or blur the open/proprietary boundary. | D2: contracts only (§9.5); US-54 stability policy with contract tests; no UI plugin loading; no direct database access. |

### 12.2 Open questions

| ID | Question | Owner / how to resolve |
|---|---|---|
| OQ-1 | Final product name. "Goldmark" is a codename; Orrery, Weft, Keelson and Plumbline are all already used by open-source projects. Needs a trademark/GitHub/domain/package-name check. | Product owner |
| OQ-2 | Is SQL Server 2025 acceptable in the reference organization (DBA standards), or is a 2022 compatibility profile needed for the MVP? | Product owner + DBA team |
| OQ-3 | Is there a third vCenter? 6 of 39 sampled AD-gap servers are VMware VMs in an unprobed vCenter (not Horizon). | Product owner |
| OQ-4 | What are the 8 "Xen HVM domU" servers: AWS EC2, Citrix Hypervisor, or other? This decides the next platform connector after AD. | Product owner |
| OQ-5 | Mapping of storage groups S1–S12 to named arrays (V7000s, FS7300, SVC I/O groups). Obtain node WWNNs from the storage team. | Storage team |
| OQ-6 | `database_guid` behavior across restore/copy: is it stable enough for database identity, or is `(instance, name)` primary? | Test during slice 17 |
| OQ-7 | Exact least-privilege permission set for the SQL collector on 2008–2022 (VIEW SERVER STATE, VIEW ANY DEFINITION, per-DB CONNECT?). | Validate in slice 17 container tests + one real non-sysadmin run |
| OQ-8 | WinRM rollout versus permanent DCOM fallback for the remaining targets. | Infrastructure team |
| OQ-9 | Confirm assumptions A1–A7 and success metrics M1–M8. | Product owner |
| OQ-10 | DCO only, or a CLA? With DCO only, outside contributions to the core can't later be relicensed (e.g. for a commercial edition of the core). Add-ons are unaffected (D2). | Product owner, **before the first external PR is merged** |

### 12.3 Future / post-MVP ideas

- **Paid apps on the platform:** endpoint monitoring (first), drift/compliance, migration planning (HCX/array refresh waves), reporting packs.
- **Multi-tenancy UI** for MSPs (the schema is already tenant-aware).
- **Connectors:**
  - UCS Manager/Intersight (blades, FIs, vNIC MAC/vHBA WWPN)
  - Dell OpenManage Enterprise/Redfish (MX chassis, PowerEdge)
  - IBM Storage Virtualize REST (volumes, hosts, I/O groups)
  - Brocade FOS/SANnav (zoning, fabrics)
  - NetApp ONTAP, PowerScale
  - Hyper-V/SCVMM, Citrix/Xen, AWS
  - NSX Manager
  - NetBox import/sync
  - ServiceNow/Jira sync
- **Linux collector** and a lightweight endpoint agent (shared with the monitoring app).
- **End-of-support/lifecycle data** (OS, SQL, firmware, e.g. ONTAP 9.4, Server 2003/2008).
- **Change-window planning,** "blast radius" scoring, maintenance calendars.
- **HA deployment,** PostgreSQL provider (if demand), hosted SaaS edition.
- **Hash-chained, tamper-evident audit log;** pseudonymized estate export for vendors and consultants.
- **Add-on platform extensions:** embedded add-on tabs (iframe + `postMessage`, FR-EX-4); the CMDB as an OIDC provider for add-on SSO in small shops (FR-EX-5); a shared design-token package so proprietary add-ons match the CMDB's look; an app catalog page.
