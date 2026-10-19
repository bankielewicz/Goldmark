# Goldmark CMDB: Claude Design prompts (PRD §7, 21 screens)

**For design review. Not part of slice 1.** Slice 1 ships only an empty SPA shell.
No functional screen is built until its design is reviewed and returned.

## How to use this file

1. Start each Claude Design session by pasting the **Shared design brief** (section A).
2. Then paste one **screen prompt** (section B). Every screen prompt assumes the brief.
3. Ask for a desktop mockup at 1440 px wide plus the extra states listed in the
   prompt. Ask for a 1024 px variant where the prompt calls for one.

Labels used below:

- **[P0]**: required for the MVP. **[P1]**: MVP-desirable, built after all P0 work.
  P1 elements inside a P0 screen are marked inline. Design them, but they must
  be removable without breaking the layout.
- Requirement IDs (US-xx, FR-xx, NFR-xx, M-x) refer to `docs/PRD.md`.
- Sample data is **fictional**. It is marked `SAMPLE DATA (fictional)` and uses
  `corp.example` names. It mirrors the *shape* of the reference estate (PRD §1.4),
  not real systems.

---

## A. Shared design brief (paste first)

```
You are designing screens for Goldmark, a self-hosted, open-source CMDB
(configuration management database) for internal IT infrastructure teams.
"Goldmark" is a working codename: keep the brand (name, logo mark, accent color)
easy to swap.

PRODUCT
- Goldmark keeps one authoritative record ("CI", configuration item) per
  real-world thing: vCenters, clusters, ESXi hosts, VMs, Windows servers, SQL
  Server instances/databases/Availability Groups, datastores, FC storage
  groups and arrays, network devices, business services, teams and people.
- Data arrives automatically from read-only collectors (vCenter, Windows/CIM,
  Active Directory, SQL Server) and CSV imports. A reconciliation engine merges
  it, and every attribute shows where its value came from (provenance).
- Core value: typed dependency relationships, redundancy-aware impact analysis
  ("what breaks if X fails": Down vs Degraded), and point-in-time history
  ("show me the estate as of last Tuesday").
- It is NOT a service desk (no tickets, approvals or SLAs) and NOT a monitoring
  tool (no metrics, charts of performance, or alerting). Do not design those.

USERS
- Dana, VMware/infrastructure admin: plans maintenance and migrations; asks
  "what breaks if...". Power user, keyboard-heavy, large screens.
- Sam, DBA: SQL instances, Availability Groups, replicas, listeners, databases.
- Pat, IT generalist at a small shop: wants simple inventory, CSV import, little jargon.
- Riley, service owner: "what does my service depend on?", owners, history.
- Ingrid, security/audit: evidence, audit log, read-only.
- Roles: Admin, Editor, Viewer, Auditor (read-only + audit log), Collector
  (service accounts only, never uses the UI). Hide actions a role cannot use;
  if a page is reached without permission, show a 403 page.

PLATFORM CONSTRAINTS (design must be buildable with these)
- React single-page app using Fluent UI React v9 (Fluent 2 design language)
  components and design tokens, Fluent UI icons, and Cytoscape.js for graph
  canvases. Map every element to a Fluent v9 component where one exists
  (Nav/NavDrawer, Toolbar, DataGrid/Table, TabList, Drawer, Dialog,
  MessageBar, Toast, Badge, Field, Input, Combobox, Dropdown, TagPicker,
  Checkbox, Switch, Slider, Breadcrumb, Popover, Tooltip, Skeleton, Spinner,
  ProgressBar, Card).
- Strict Content Security Policy: no web fonts or images from external CDNs
  (use the Segoe UI / system font stack and bundled icons), no embedded
  third-party widgets or iframes, no rendering of user-supplied HTML.
  Add-on apps appear only as status badges and "Open in {app}" links.
- Desktop browsers (current Edge, Chrome, Firefox). No mobile app.

VISUAL DIRECTION
- Calm, dense, information-first enterprise tool. Data tables and side panels
  over decorative cards. Comfortable default density, plus a compact density option.
- Light and dark themes (Fluent webLightTheme / webDarkTheme); neutral palette
  with one brand accent (a muted gold/amber).
- Status semantics, always icon + text and never color alone:
    Down      = danger (red)    + ErrorCircle icon + "Down"
    Degraded  = warning (amber) + Warning icon     + "Degraded"
    OK/Active = success (green) + CheckmarkCircle  + label
    Not seen / Unknown / Stale = neutral (grey) + Clock or Question icon + label
  Lifecycle badges: Planned, Active, Retiring, Retired (Retired is muted).
  Provenance badges name the source, e.g. "vCenter vc01", "Manual", "CSV hr-assets".

APP SHELL (all signed-in screens)
- Left navigation (collapsible to icons), grouped:
    Overview: Dashboard
    Inventory: Search, Graph explorer, Impact analysis, Business services
    Data hygiene: Review queue, Data quality, Storage groups, Change history
    Integration: Sources & collectors, CSV import, API tokens, Webhooks,
                 Apps & integrations [P1]
    Administration: Types & attributes, Users & roles, Audit log, Settings
- Top bar: instance name, global search box (Enter goes to /search?q=...),
  "As of" control, theme toggle, user menu (profile, MFA, sign out).
- Skip-to-content link; page title (h1) + breadcrumb on every page.

"AS OF" TIME TRAVEL (US-42)
- Any read view can be shown as of a past timestamp (UTC, with local-time hint).
  When active, show a persistent banner at the top of the content: "Viewing as
  of 2026-10-06 14:00 UTC (read-only)" with a "Return to now" button. All
  create, edit and delete actions are disabled, with a tooltip explaining why.

STABLE DEEP LINKS (US-53)
- /ci/{id}, /ci/{id}?tab={tab}, /impact?ci={id}&ci=..., /search?q=... must
  each map to a screen state. A CI that was merged into another redirects and
  shows an info MessageBar: "This CI was merged into <name>. You were
  redirected." An unknown id shows a friendly Not-found page (illustration-free,
  with search box and link to Dashboard), never an error stack.

STATES: design these for every screen
- Loading: Skeleton rows/blocks shaped like the content (not a lone spinner);
  regions marked busy for assistive tech.
- Empty: one sentence explaining why it's empty + the primary next action.
- Error: Fluent MessageBar (intent error) with plain-language cause, a Retry
  action, and a copyable "Reference ID" (trace id). Never show stack traces.
- Success: Toast for completed actions; inline confirmation where relevant.
- Permission denied: 403 page or disabled control with an explanation.
- Concurrency conflict: when someone else changed the record (HTTP 412),
  show a Dialog offering "Review their changes" / "Discard mine".
- Stale data: when a collector source is stale, show a warning badge where its values appear.

SECRETS UX
- Tokens, webhook secrets and MFA recovery codes are shown ONCE: dedicated
  panel, monospace value, Copy button, "I have stored this" confirmation
  before closing, and a warning that it cannot be shown again.
- Destructive actions (revoke, retire, delete mapping, disable app) use a
  confirmation Dialog. High-impact ones (merge CIs, revoke all tokens) require
  typing the object's name.

ACCESSIBILITY (NFR-A11Y-1/2/3): WCAG 2.2 AA is a hard requirement
- Every function works by keyboard alone; logical focus order; visible focus
  indicator (Fluent focus ring); no keyboard traps except modal focus trap.
- Contrast: text 4.5:1, large text and UI components/graphics 3:1, in both themes.
- Target size at least 24x24 CSS px (2.5.8). Do not rely on hover alone.
- Every graph or chart has an equivalent, linked table view.
- Tables: real header cells, sort state exposed, row actions reachable by keyboard.
- Announce asynchronous results (search done, import finished) via a polite live region.
- Respect reduced motion. Content reflows at 400% zoom (1280 px viewport);
  wide tables scroll inside their own region rather than the page.

RESPONSIVE
- Primary: 1280-1920 px desktop. Must remain fully usable at 1024 px (nav
  collapses to icons, side panels become overlay drawers). 768 px tablet:
  read-only use is acceptable. Phones: only Sign-in and CI detail need to be readable.

SAMPLE DATA
- Use only fictional data under *.corp.example, and label it in the mockup
  footer as "Sample data (fictional)". Reference estate shape: 2 vCenters,
  10 clusters, ~76 ESXi hosts, ~2,178 VMs, ~1,600 AD server objects,
  14 SQL Server instances (7 with Availability Groups), 12 FC storage groups
  (S1-S12), ~5,300 core CIs.
```

---

## B. Screen prompts

### 1. Sign-in [P0] (US-03; US-05 [P1]; NFR-SEC-2)

```
Using the Goldmark shared brief, design the Sign-in screen (no app shell).

PURPOSE: Users authenticate with username, password and a TOTP code (MFA is
mandatory for local accounts). As a user, I sign in so I can use Goldmark.

LAYOUT: Centered card (~400 px) on a neutral background; product mark and
instance name ("Goldmark: SAMPLE Contoso Infrastructure") above the card.
Footer: version string and "Sample data (fictional)" note.

STEP 1 (credentials): Username field, Password field (show/hide toggle),
"Sign in" primary button. [P1] Divider "or" + "Sign in with Microsoft" button
(Microsoft logo from the bundled icon set), only when OIDC is configured.
STEP 2 (MFA): "Enter the 6-digit code from your authenticator app", one-time
code input (numeric, autocomplete one-time-code), "Verify" button, link "Use a
recovery code instead" (switches to a text field), "Back".

STATES
- Loading: button shows inline spinner and is disabled; inputs read-only.
- Error (wrong credentials): generic MessageBar "Username or password is
  incorrect." Do not reveal which one was wrong.
- Error (wrong code): "That code didn't work. Codes change every 30 seconds."
- Locked out: MessageBar warning "This account is temporarily locked after 5
  failed attempts. Try again in 15 minutes or contact an administrator."
  (duration comes from configuration).
- Session expired: info MessageBar "Your session expired. Sign in again."
- Setup not completed: redirect message "Goldmark isn't set up yet" with a
  link to first-run setup.
- Success: navigate to the page originally requested (deep link preserved).

ACCESSIBILITY: Labels visible (no placeholder-only labels); errors linked to
fields and announced; focus moves to the code field on step 2 and to the
MessageBar on error; works with password managers.
RESPONSIVE: card becomes full-width under 480 px.
```

### 2. First-run setup [P0] (US-02, US-03)

```
Using the Goldmark shared brief, design the First-run setup wizard (no app
shell). It appears only until the first admin exists; afterwards the URL shows
"Setup is already complete" with a link to Sign-in.

PURPOSE: The installer creates the first admin account, protected by a
one-time setup token printed in the container log, and must enrol TOTP before
reaching any other page.

LAYOUT: Centered wizard (~560 px) with a 4-step progress indicator:
1 Setup token  2 Admin account  3 Two-factor authentication  4 Recovery codes.

STEP 1: Explanation "Find the setup token in the Goldmark container log
(docker compose logs app)", copyable example command, token input, Continue.
STEP 2: Username, display name, email (optional), password + confirm, live
password-rule checklist (length etc., icon + text), Continue.
STEP 3: QR code, manual key in monospace groups with Copy, list of compatible
authenticator apps (text only), 6-digit verification input, Verify.
STEP 4: 10 recovery codes (SAMPLE: "7KQ2-M9XD", ...) in a monospace grid,
Download .txt, Copy all, Print; checkbox "I have stored these codes safely";
Finish (disabled until checked). Warning: "These codes are shown only once."
DONE: Success panel "Goldmark is ready" + "Go to dashboard".

STATES: invalid/expired token error; token already used ("Setup was completed
by someone else"); weak password inline errors; wrong TOTP code; network
error with Retry and Reference ID; loading on each Continue.
ACCESSIBILITY: step changes move focus to the step heading and are announced;
the QR code has a text alternative (the manual key); the progress indicator is
not color-only.
RESPONSIVE: single column; QR code at least 180 px.
```

### 3. Dashboard [P0] (US-62; screen 3)

```
Using the Goldmark shared brief, design the Dashboard (app shell, nav item
Overview > Dashboard).

PURPOSE: Estate health at a glance: is collection working, what needs
attention, what changed recently.

LAYOUT (desktop): page title + "As of" status. Row 1: summary cards (CIs by
type, top 8 types + "View all", e.g. SAMPLE DATA: VirtualMachine 2,178,
Server 1,633, HypervisorHost 76, SqlDatabase 224, Datastore 493,
StorageGroup 12). Row 2: Collector health tiles, one per source (name,
kind icon, last run "12 min ago", items 2,181, status OK/Errors/Stale with
icon + text, errors by class e.g. "AccessDenied 1, Timeout 3", staleness
"expected every 1 h"). Row 3, two columns: left "Needs attention" (Review
queue: 7 open, link; Data quality: AD-not-in-hypervisor 195, VMs without OS
link 41, Not seen 12, each a link); right "Recent changes" feed (time,
actor/source badge, CI link, change summary such as "lifecycle Active ->
Retiring", "+2 relationships"), with "View change history".

INTERACTIONS: every number links to the filtered list; tiles open the
Sources & collectors run history; refresh button with last-updated time.

STATES
- Empty (fresh install): "No data yet" with three next steps: Register a
  collector source, Import a CSV, Load the sample dataset.
- Loading: skeleton cards and tiles.
- Partial: one source stale → its tile shows the neutral "Stale" badge and a
  warning MessageBar summarises "1 source hasn't reported for 26 h".
- Error: per-widget error with Retry; the rest of the page still renders.
- As-of active: dashboard shows counts as of that time; feed ends at that time.

ACCESSIBILITY: cards and tiles are headed regions; numbers have text labels;
the feed is a list; no chart is required. If you add a sparkline, it needs a table equivalent.
RESPONSIVE: 1024 px → tiles wrap to 2 columns; feed moves below.
```

### 4. CI search / list [P0] (US-10/11/12/13, US-42, US-53 `/search?q=`, NFR-PERF-2)

```
Using the Goldmark shared brief, design the CI search / list screen
(Inventory > Search). URL: /search?q=... (stable deep link).

PURPOSE: Find any CI fast and narrow the list with filters.

LAYOUT: search input at the top (prefilled from ?q), result count ("1,248
CIs"), toolbar (Saved filters dropdown, Save current filter, Column chooser,
Export CSV, "As of" picker, Show retired toggle). Left filter panel
(collapsible): CI type (with counts), Lifecycle (Planned/Active/Retiring/
Retired; Retired off by default), Owner (team/person picker), Source
(vCenter vc01, vCenter vc02, AD corp.example, SQL collector, CSV ...), Not seen
only. Main: DataGrid with columns Name, Type, Lifecycle, Owner, Primary
source, Last seen, plus chosen attribute columns (e.g. os, platform, cluster).
Cursor paging: "Load more" + "Showing 1-100".

SAMPLE DATA (fictional) rows:
- app-erp-db01 | VirtualMachine | Active | Team: ERP Platform | vCenter vc01 | 9 min ago
- SQLPRD01\ERP | SqlInstance | Active | Person: Sam Ortega | SQL collector | 1 h ago
- esx-mx-014.corp.example | HypervisorHost | Retiring | Team: Virtualization | vCenter vc02 | 9 min ago
- hcx-ma-01 | Appliance (hcx-mobility-agent) | Active | (none) | vCenter vc01 | 9 min ago

INTERACTIONS: row click or Enter opens /ci/{id}; multi-select for Export;
column sort; filters update the URL so the view can be shared; saved filters
are per user.

STATES: no results ("No CIs match 'sql99'", with Clear filters / Search all
types); first-use empty state (no CIs at all, links to Sources and CSV
import); loading skeleton rows; error MessageBar with Retry; export in
progress toast, then "Export ready"; as-of banner active (read-only).
ACCESSIBILITY: filter panel is a labelled region; result count announced
after each search; sort state on headers; Export is keyboard reachable.
RESPONSIVE: 1024 px → filter panel becomes a Drawer opened by a "Filters (3)" button.
```

### 5. CI detail [P0 + P1 badges/links] (US-11/12/13/22/42/43/53; US-55/57 [P1]; FR-RC-3/5/6)

```
Using the Goldmark shared brief, design the CI detail page. URL /ci/{id} and
/ci/{id}?tab=attributes|relationships|components|history|identifiers|impact.
The URL is a stable public contract.

HEADER: CI name (h1), type badge with icon, lifecycle badge, owner (team and/or
person links), "Last seen 9 min ago by vCenter vc01". [P1] External status
badges from registered apps, e.g. "Monitoring: Critical", with app name and
updated time, linked out. [P1] "Open in {app}" link buttons (external-link
icon, open in new tab). Actions: Edit (Editor), Change lifecycle, Retire,
View in graph, Run impact. Breadcrumb: Search > VirtualMachine > app-erp-db01.

TABS (TabList; state in ?tab=):
1 Attributes: two-column property grid grouped (Identity, Hardware/OS,
  Placement, Ownership, Custom). Each value shows a provenance badge (winning
  source) and last-seen time; a "2 sources" chip opens a Popover listing every
  source's value, which one won and why (precedence). Collector-owned fields
  are read-only, with lock icon + source name. "Override" opens a Dialog
  requiring a reason; overridden values show an "Overridden" badge and the
  reason (FR-RC-3).
2 Relationships: grouped by relationship type and direction ("Depends on" /
  "Depended on by"), showing related CI, type, redundancy group (e.g.
  "AG-ERP replicas"), source and since-date. Add relationship (Editor).
3 Components: tables by kind (NICs, HBA ports, LUN paths, disks), e.g. NIC
  MAC 00:50:56:aa:bb:01, network "dvPG-App-120".
4 History: vertical timeline (who/what, when, summary) + side-by-side diff of
  attributes and relationships for the selected change; filter by source/user.
5 Identifiers: scheme, normalized value, scope, source, since (e.g.
  BIOS-UUID-SWAPPED 4221a5..., AD objectGUID, vCenter instanceUuid).
6 Impact: "What depends on this?": a short list of dependents with
  Down/Degraded preview + "Open full impact analysis".

SAMPLE DATA (fictional): VirtualMachine app-erp-db01, vCenter vc01, cluster
CL-PROD-02, host esx-mx-014, datastore DS-S3-ERP-01 (StorageGroup S3), OS
Windows Server 2022, linked Server CI APP-ERP-DB01 (os_of), SQL instance
SQLPRD01\ERP hosted_on that server, member of AG-ERP.

STATES: loading skeleton per tab; CI not found → friendly Not-found page;
merged CI → redirect + info MessageBar naming the surviving CI; Retired CI →
muted header + "Retired on 2026-09-30" banner; edit conflict (412) Dialog;
as-of view → banner, read-only, values as they were; tab-level errors with Retry.
ACCESSIBILITY: tabs follow the ARIA tabs pattern with ?tab sync; provenance
popovers open on click/Enter (not hover only); diff uses text markers
(Added/Removed/Changed) besides color.
RESPONSIVE: 1024 px → property grid becomes single column; header actions
collapse into an overflow menu.
```

### 6. Graph explorer [P0] (US-40, US-42, NFR-A11Y-2)

```
Using the Goldmark shared brief, design the Graph explorer (Inventory > Graph
explorer, also opened from a CI's "View in graph").

PURPOSE: See a CI's neighborhood to depth N (1-6) and explore dependencies visually.

LAYOUT: Toolbar: focus CI picker, Depth slider 1-6 (with numeric value),
Direction (Dependencies / Dependents / Both), CI type filter (multi-select
TagPicker), Relationship type filter, Layout toggle (Hierarchical / Force),
"As of" control, View toggle Graph | Table, Fit, Zoom in/out, Export (PNG of
canvas and CSV of table). Main: Cytoscape canvas. Nodes are type icons with
labels; edges are labelled with the relationship type on hover/focus;
redundancy groups are a dashed hull or shared edge color, plus a legend entry.
Right Drawer: details of the selected node (name, type, lifecycle, owner, key
attributes, "Open CI" link).

SAMPLE DATA (fictional), depth 3 from app-erp-db01: ->runs_on esx-mx-014
->member_of_cluster CL-PROD-02; ->uses_datastore DS-S3-ERP-01 ->backed_by
StorageGroup S3 ->part_of_array FS7300-A; <-os_of APP-ERP-DB01 <-hosted_on
SQLPRD01\ERP <-replica_on AG-ERP (redundancy group with SQLPRD02\ERP).

TABLE VIEW (equivalent, required): rows = edges (From, Relationship, To,
Redundancy group, Depth), sortable, each CI a link; same filters.
INTERACTIONS: click/Enter on a node selects it; double-click or "Open CI"
navigates; arrow keys move between nodes in the canvas's keyboard mode; Esc clears selection.

STATES: loading (skeleton canvas + "Loading 3 levels..."); too large (>500
nodes): warning MessageBar suggesting lower depth or filters, table view still
available; empty (no relationships); error with Retry; as-of banner.
ACCESSIBILITY: the canvas has an accessible name and a "Switch to table view"
control placed before it; table view has full parity; legend uses shape + label, not color alone.
RESPONSIVE: 1024 px → details Drawer overlays the canvas; toolbar wraps into two rows.
```

### 7. Impact analysis [P0; save scenario P1] (US-41, US-42, US-44 [P1], FR-IM-1..4, M8)

```
Using the Goldmark shared brief, design the Impact analysis screen.
URL /impact?ci={id}&ci={id} (stable deep link).

PURPOSE: "What breaks if these CIs fail?" Every dependent CI is marked Down
or Degraded, with the path that explains it; redundancy is honored.

LAYOUT: Left panel "Failed CIs": search + multi-select (TagPicker) of CIs,
chips for selected items, depth limit (default 10, max 20), "As of" control,
Run button. [P1] Saved scenarios dropdown + "Save scenario" (name,
description), e.g. "SAN group S3 maintenance". Main: summary bar ("Down 41 ·
Degraded 17 · Evaluated 1,204 CIs in 0.8 s"), result DataGrid (Status with
icon+text, CI, Type, Owner, Depth, Why), Export CSV. Selecting a row expands
the explanation path: "app-erp-db01 Down ← uses_datastore DS-S3-ERP-01 Down ←
backed_by StorageGroup S3 (failed)". For Degraded: "AG-ERP Degraded:
1 of 2 replicas down (redundancy group AG-ERP replicas)". Toggle "Graph
overlay" opens the graph with failed/Down/Degraded styling (patterns + icons, not color only).

SAMPLE DATA (fictional): failed = StorageGroup S3; results include 14 VMs
Down, 2 SQL instances Down, AG-ERP Degraded, BusinessService "ERP" Degraded.

STATES: initial empty ("Choose one or more CIs to simulate a failure");
running (progress + Cancel); no impact ("Nothing depends on these CIs");
depth limit reached warning; Retired CIs ignored note; error with Reference
ID; as-of banner ("Impact computed as of ...").
ACCESSIBILITY: results announced ("41 Down, 17 Degraded"); explanation paths
are text lists; status never color-only.
RESPONSIVE: 1024 px → left panel collapses above results.
```

### 8. Business service editor [P0] (US-14, US-13, US-12, FR-IM-1)

```
Using the Goldmark shared brief, design the Business service editor
(Inventory > Business services, list + create/edit).

PURPOSE: Editors define business services (e.g. "ERP", "Payroll") and link
them to the CIs they depend on, so impact analysis reaches the business layer.

LIST VIEW: DataGrid (Service, Owner, Criticality, Lifecycle, Dependencies count, Impact status), "New service".
EDIT VIEW: Form: Name, Description, Owner team, Owner person, Criticality
(Dropdown: Critical / High / Medium / Low), Lifecycle (Planned / Active /
Retiring / Retired). Dependencies section: search-and-add picker (CI search
with type filter: VMs, Servers, SQL databases, AG listeners), table of
dependencies (CI, Type, Relationship "depends_on", Redundancy group (optional
free text with suggestions), Remove). Right side "Quick impact preview": top
dependencies and their current status, plus "Run full impact".
Save / Cancel; unsaved-changes guard.

SAMPLE DATA (fictional): Service "ERP" (Critical, Team ERP Platform, owner
Riley Chen) depends on AG listener ERP-LSNR, SQL database ERP_PROD, VMs
app-erp-web01/02 (redundancy group "ERP web tier").

STATES: empty list ("No business services yet. Create the first one."); validation
errors (name required/unique); save success toast; conflict (412) Dialog;
loading; error; read-only for Viewer/Auditor; as-of read-only.
ACCESSIBILITY: picker results announced; table row remove buttons have CI-specific labels.
RESPONSIVE: preview panel moves below the form at 1024 px.
```

### 9. Review queue [P0] (US-23, FR-RC-4, FR-RC-5)

```
Using the Goldmark shared brief, design the Review queue (Data hygiene >
Review queue).

PURPOSE: Resolve reconciliation conflicts that rules could not decide, instead of
auto-merging or auto-duplicating.

LAYOUT: master-detail. Left: list filtered by Kind (Two candidate CIs,
Identifier claimed by two CIs, VM↔OS link conflict), Status (Open/Resolved),
Source, Age; each item shows kind icon, short title, source, age. Bulk-select
checkboxes. Right: Evidence panel: side-by-side candidate CIs (name, type,
key attributes, identifiers with scheme/value/source, last seen), with
differences highlighted using text markers. Incoming observation from the
source at the top. Actions: Merge (choose survivor; preview of re-pointed
relationships/identifiers; type-to-confirm), Link, Create new CI, Reject. Each
action requires a comment (optional for Reject). Audit trail list at the bottom of the panel.

SAMPLE DATA (fictional): "BIOS UUID 4221a5…e0 matches 2 powered-on VMs" -
app-erp-db01 (vc01) vs app-erp-db01-old (vc02); "AD objectGUID 7f3c… claimed
by APP-ERP-DB01 and APP-ERPDB01".

STATES: queue empty ("Nothing to review. Reconciliation is keeping up.");
item already resolved by someone else (idempotent: info MessageBar, refresh);
bulk action partial failure summary; loading; error with Reference ID.
ACCESSIBILITY: master-detail works by keyboard (list → panel → actions);
comparisons are readable as a table, not just visual alignment.
RESPONSIVE: 1024 px → evidence panel opens as a full-width Drawer.
```

### 10. Storage group mapping [P0] (US-35, ID-FC, M4)

```
Using the Goldmark shared brief, design the Storage group mapping screen
(Data hygiene > Storage groups).

PURPOSE: FC storage groups are derived automatically (connected components of
target WWNNs). An editor gives each group a named StorageArray; the mapping
persists across collector runs.

LAYOUT: DataGrid of groups: Group (S1...S12), WWNN set (first two shown +
"+2 more" Popover; monospace, 16-hex normalized, colon-formatted), LUNs,
Datastores, Hosts, Mapped array (or "Unmapped" neutral badge), Last derived.
Selecting a group opens a Drawer: full WWNN list with Copy, counts, sample
datastores, "Assign to array": pick existing StorageArray (Combobox) or create
new (Name, Kind block/nas, Family e.g. "IBM FlashSystem 7300"), Confirm. Also
"Unmap" with confirmation. A header summary: "12 groups · 10 mapped · 493/493
SAN datastores mapped to a group".

SAMPLE DATA (fictional): S3 WWNNs 50:05:07:68:0b:21:4a:10, 50:05:07:68:0b:21:4a:11;
LUNs 48; datastores 41; hosts 22; mapped to "FS7300-A (block)".

STATES: no groups yet (vCenter collector not run: link to Sources); group
WWNN set grew since mapping (info badge "Group grew: 2 new WWNNs", mapping
kept); conflict on save; loading; error.
ACCESSIBILITY: WWNNs readable by screen readers (grouped, not one long string); Copy buttons labelled.
RESPONSIVE: Drawer becomes full-width at 1024 px.
```

### 11. Data quality [P0; CDP section P1] (US-24, US-33, US-36 [P1], M3)

```
Using the Goldmark shared brief, design the Data quality screen (Data
hygiene > Data quality).

PURPOSE: Hygiene reports that show gaps between sources.

LAYOUT: Left list of reports with counts; main area shows the selected report
as a DataGrid with Export CSV and a short explanation of the rule:
- In AD but not in any hypervisor (195): Name, OS, OU, Platform guess
  (physical/xen/vmware-unprobed), Last logon.
- In AD but DNS does not resolve (23)
- VMs without OS link (41): VM, vCenter, Power state, Tools status, reason
  (no UUID match / multiple candidates → link to Review queue).
- Stale / Not seen (12): CI, Source, Missed runs, Last seen, suggested action
  "Mark Retiring".
- Duplicate suspects (5)
- [P1] CDP/LLDP neighbors not used for links: summary by class
  (switch-class linked, host-class ignored e.g. ISE VMs, hypervisor-class
  ignored, unknown), with an explanation that only switch-class neighbors create links.

INTERACTIONS: row → CI detail; bulk "Mark Retiring" for Not-seen (Editor,
confirmation); filters per report; as-of supported.
STATES: report with zero rows ("No AD objects are missing from hypervisor
inventory"); report unavailable because its source is not configured
("Connect an Active Directory source to enable this report"); loading; error.
ACCESSIBILITY: report list is a nav list with counts in text; tables follow the standard.
RESPONSIVE: report list becomes a Dropdown at 1024 px.
```

### 12. Sources & collectors [P0] (US-30, US-62, US-24, US-34, FR-RC-2/3, FR-CO-2/7)

```
Using the Goldmark shared brief, design Sources & collectors (Integration >
Sources & collectors): list, register wizard, and source detail.

LIST: DataGrid: Source name, Kind (vCenter / Windows / Active Directory / SQL
Server / CSV) with icon, Scope ("vCenter vc01, full inventory"), Last run,
Result (OK / Errors / Stale, icon+text), Items, Errors by class, Next expected.
"Register source".
REGISTER (Dialog/wizard): Name, Kind, Scope key, Absence threshold (default 3
missed full runs), then "Issue ingest token" → shown-once token panel
(prefix sxt_, scope ingest:write bound to this source) and the exact
PowerShell install command for a scheduled task under a gMSA (monospace,
Copy). Explain that Goldmark stores no vCenter/AD/SQL credentials; they stay
on the collector host.
DETAIL: tabs Overview (settings: scope, absence threshold, attribute
precedence order: drag-to-reorder list Manual > SQL > Windows > vCenter > AD >
CSV, with keyboard move up/down buttons), Run history (per run: start,
duration, counts created/updated/unchanged/review, errors by class
AccessDenied/Timeout/WinRMUnavailable/Unsupported OS), Tokens (rotate, revoke).
SQL sources show a warning badge if any instance config uses
TrustServerCertificate=true ("Certificate validation disabled for 1 instance").

SAMPLE DATA (fictional): vCenter vc01 (2,181 items, OK, 9 min ago), Windows
collector (1,412 targets, Errors: AccessDenied 1, Timeout 3), AD
corp.example (Stale: last run 26 h ago).

STATES: none registered (explain collectors + "Register source"); token shown
once; run with errors expanded; stale source; loading; error; revoke confirmation.
ACCESSIBILITY: precedence reorder must work without drag (buttons); token panel follows the secrets UX.
RESPONSIVE: detail tabs scroll horizontally at 1024 px.
```

### 13. CSV import wizard [P0] (US-25, M5)

```
Using the Goldmark shared brief, design the CSV import wizard (Integration >
CSV import). It is a key path for small shops (Pat).

STEPS: 1 Upload (drag-and-drop zone + file button; size limit; encoding and
delimiter detection shown), 2 Mode: "CIs of one type" (pick CI type) or
"Relationships" (from-key, type, to-key, optional redundancy group), 3 Map
columns (CSV header → attribute/identifier, sample values per column,
auto-suggested mappings, required fields marked, unmapped columns ignored
with a note), 4 Dry run (counts: Create 120 / Update 34 / Unchanged 5 /
Conflicts 3 → link to details; row-level issues table: row, column, problem),
5 Commit (import name → source "csv:<name>", Commit button, progress), Done
summary. Side link: "Import history" (list of past imports with counts,
user, time, download of original file name only).

SAMPLE DATA (fictional): file "branch-office-servers.csv", 159 rows, columns
hostname, serial, manufacturer, owner_team, location.

STATES: invalid file/encoding error; mapping incomplete (Next disabled with
reason); dry-run conflicts (explain they go to the Review queue); commit in
progress (async, can leave page; toast when done); commit failed partway
(what was applied); success; loading; first-time hint "Try the sample dataset".
ACCESSIBILITY: drop zone has a keyboard-operable button; mapping table usable
by keyboard; progress announced.
RESPONSIVE: mapping table scrolls in its region at 1024 px.
```

### 14. Types & attributes admin [P0] (US-10, FR-CM-1/2, FR-IM-1, §9.3/9.4)

```
Using the Goldmark shared brief, design Types & attributes (Administration >
Types & attributes), Admin only.

LAYOUT: Left list of CI types grouped (Location & ownership, Compute, Network,
Storage, Clustering & SQL, Business), built-in types with a lock badge, plus
"New custom type". Main for the selected type: Attributes DataGrid (Name,
Data type: string/number/bool/date/enum/CI-reference, Required, Enum values,
Reference target type, Promoted (filterable column), Owner source policy,
Built-in), "Add attribute" Dialog (type-specific fields: enum values editor;
CI-reference target). Promote toggle explains that promotion creates an
index by migration and runs in the background (status: Pending / Applied).
Second tab: Relationship types: Name, From type, To type, Impact
propagation (None / Down), Built-in, with descriptions (e.g. replica_on uses
redundancy groups: AG Down only if all replicas Down).

STATES: built-in attributes not deletable (disabled with reason); deleting a
custom attribute in use requires typed confirmation and shows the count of
CIs affected; promotion pending; validation errors; loading; error.
ACCESSIBILITY: lock badges have text; dialogs trap focus.
RESPONSIVE: type list becomes a Combobox at 1024 px.
```

### 15. Change history (global) [P0] (US-43, US-42, screen 15)

```
Using the Goldmark shared brief, design the global Change history (Data
hygiene > Change history).

LAYOUT: Filters: time range (presets + custom, UTC), CI type, source,
user, change kind (attribute / relationship / lifecycle / merge),
CI search. Results: DataGrid (Time, Actor (user or source badge), CI, Type,
Change summary). Selecting a row opens a diff panel (before/after per
attribute; relationships added/removed), with links "Open CI" and "View CI as
of this time". Export CSV of the filtered result.

SAMPLE DATA (fictional): 2026-10-15 09:12 UTC · vCenter vc02 · esx-mx-014 ·
lifecycle Active → Retiring; 2026-10-15 08:40 · user dana.k · ERP · +depends_on ERP-LSNR.

STATES: no changes in range; very large result (paged, "narrow your filters"
hint); loading; error; export progress.
ACCESSIBILITY: diff markers in text (Added/Removed/Changed); time shown in UTC with local tooltip.
RESPONSIVE: diff panel becomes a Drawer at 1024 px.
```

### 16. Audit log [P0] (US-07, NFR-LOG-3)

```
Using the Goldmark shared brief, design the Audit log (Administration > Audit
log), visible to Admin and Auditor, read-only for everyone.

LAYOUT: Info MessageBar: "The audit log is append-only. Entries cannot be
edited or deleted." Filters: actor (user, token, collector source, system),
action (sign-in success/failure, MFA enrolment, CI edit, relationship edit,
review decision, token created/revoked/used, role change, settings change,
webhook change), target, time range, source IP. DataGrid: Time (UTC), Actor
(type badge + name), Action, Target (link), IP address, Result. Row expands to
show details (JSON-like key/value list rendered as a definition list, not raw JSON).
Export CSV (shows the filter and row count before exporting).

SAMPLE DATA (fictional): 2026-10-16 07:58 · user ingrid.m · Sign-in (MFA) ·
Success · 10.20.4.17; 2026-10-16 07:30 · token sxt_…9f2c (vc01 collector) ·
Ingest batch accepted · Success.

STATES: no entries in range; loading; error; export; retention notice
("Entries older than 365 days are purged per retention settings").
ACCESSIBILITY: expandable rows by keyboard; results count announced.
RESPONSIVE: IP column hides behind the row expander at 1024 px.
```

### 17. Users & roles [P0; OIDC mappings P1] (US-04, US-03, US-05 [P1])

```
Using the Goldmark shared brief, design Users & roles (Administration > Users & roles), Admin only.

LAYOUT: DataGrid: User, Display name, Roles (Admin/Editor/Viewer/Auditor as
badges), MFA status (Enrolled / Not enrolled, icon+text), Status (Active /
Locked until 10:45 / Disabled), Last sign-in, Sign-in method (Local / [P1]
Microsoft). "Add user" Dialog (username, display name, email, roles,
temporary password or invite flow, MFA required notice). User detail Drawer:
edit roles (checkbox list with role descriptions), Unlock, Reset MFA
(confirmation: user must re-enrol), Disable/Enable, Reset password.
[P1] Tab "OIDC group mappings": table of IdP group ID/name → role, add/remove.
Note: Collector role is for service tokens only and is not assignable to people here.

STATES: last Admin cannot be removed/disabled (explained); locked user;
loading; error; success toasts; self-edit restrictions (cannot remove own Admin role).
ACCESSIBILITY: role checkboxes with descriptions; status not color-only.
RESPONSIVE: Drawer full-width at 1024 px.
```

### 18. API tokens [P0] (US-06, FR-IN-4, FR-EX-5)

```
Using the Goldmark shared brief, design API tokens (Integration > API tokens), Admin only.

LAYOUT: DataGrid: Name, Prefix (sxt_…9f2c), Scopes (badges: cis:read,
ingest:write …), Bound source (if any), Created by, Created, Expires (with
"expires in 12 days" warning badge), Last used, Status (Active / Expired /
Revoked). "Create token" Dialog: Name, Scopes (checkbox list with
descriptions), Source binding (required for ingest:write), Expiry (presets
30/90/180 days, custom, max limit), Create → shown-once secret panel
(secrets UX). Row actions: Revoke (confirmation), View usage (last 20 uses
from audit log).

SAMPLE DATA (fictional): "vc01 collector" · ingest:write · source vCenter
vc01 · expires 2027-01-14 · last used 9 min ago.

STATES: none yet; created (secret shown once); revoked; expired; loading; error.
ACCESSIBILITY: secret panel focus management and Copy confirmation announced.
RESPONSIVE: secondary columns collapse into the row expander.
```

### 19. Webhooks [P0; replay P1] (US-51, US-52 [P1], FR-EV-1..4)

```
Using the Goldmark shared brief, design Webhooks (Integration > Webhooks), Admin only.

LIST: DataGrid: Endpoint URL (https only), Event types (badges: ci.created,
ci.updated, ci.retired, ci.merged, relationship.created, relationship.removed,
review.created, source.run.completed), Status (Active/Paused), Last delivery
result, Failure rate 24 h. "New subscription".
CREATE/EDIT: URL (validation: https required), event type checkboxes, Active
switch; on create, the signing secret is shown once (HMAC-SHA256), with a
"Verify signatures" help snippet (header X-Goldmark-Signature: t=…,v1=…, reject
skew > 5 min). Actions: Rotate secret (shown once), Send test delivery
(result inline: status code, latency), Pause.
DELIVERY LOG tab: DataGrid: Time, Event type, Event ID, Attempt, Status
(Delivered / Retrying, next attempt at 10:42 / Dead-lettered), Response code,
Duration; row detail with request headers (signature redacted) and response
status; Retry now. [P1] "Replay since…" (date-time picker bounded by
retention, confirmation with count).

STATES: none yet; endpoint failing (warning badge, exponential backoff
explained); dead-lettered deliveries; test delivery failure; loading; error.
ACCESSIBILITY: status not color-only; retry buttons labelled with event ID.
RESPONSIVE: delivery log scrolls within its region at 1024 px.
```

### 20. Settings [P0; OIDC and backup P1] (screen 20, M7, NFR-SEC-5, US-61 [P1], US-05 [P1])

```
Using the Goldmark shared brief, design Settings (Administration > Settings), Admin only.

SECTIONS (left sub-nav, one form each with its own Save):
- General: Instance name.
- TLS (read-only information): certificate in use (Generated self-signed /
  Operator-supplied), SHA-256 fingerprint (monospace, Copy), subject names,
  expires (with warning when < 30 days), HSTS status (On/Off/Automatic) with
  a link to the docs. Changes are made in deployment configuration, not here
  (say so).
- [P1] Sign-in with Microsoft (OIDC): tenant/authority, client ID, client
  secret (write-only; shows "Set • updated 2026-10-01", Replace button), test sign-in.
- Retention: change history (years), audit log (days, minimum 365),
  webhook deliveries / change feed (days, default 30), with impact notes.
- [P1] Backups: target path, schedule, retention, last backup result banner.
- Telemetry: opt-in switch, OFF by default, with a plain description of
  exactly what would be sent (M7).

STATES: unsaved-changes guard; validation errors; saved toast; secret fields
never re-display values; loading; error.
ACCESSIBILITY: each section is a form with its own heading; switches have visible labels.
RESPONSIVE: sub-nav becomes a Dropdown at 1024 px.
```

### 21. Apps & integrations [P1] (US-55, US-57, FR-EX-1/3/5, D2)

```
Using the Goldmark shared brief, design Apps & integrations (Integration >
Apps & integrations). The whole screen is P1, Admin only.

PURPOSE: Register external add-on apps (e.g. an endpoint-monitoring product).
They integrate only through the API, webhooks, annotations and links. No add-on
code ever runs inside Goldmark's UI.

LIST: Cards or DataGrid: App name, Publisher, Status (Enabled/Disabled),
Annotation namespace (e.g. "monitoring"), Allowed scopes, Tokens (count),
Last activity. "Register app".
REGISTER/EDIT: Name, Publisher, Annotation namespace (unique, lowercase),
Allowed token scopes (checkboxes), Link templates per CI type (table: CI type,
Label "Open in Monitor", URL template
https://monitor.example/host/{ciId}, with a preview using a sample CI),
App tokens (issue, shown once; revoke), Disable app (confirmation: "Revokes
its tokens and hides its links and badges; annotations are kept until purged"),
Purge annotations (typed confirmation).
Also show how it appears on CI detail: a small preview of a status badge
"Monitoring: Critical · 2 min ago" and the "Open in Monitor" link button.

STATES: no apps registered (explain the integration model briefly); namespace
already taken; invalid URL template (must be https, must include a
placeholder); disabled app; loading; error.
ACCESSIBILITY: preview components labelled as previews; link-template table keyboard-editable.
RESPONSIVE: edit form single column at 1024 px.
```

---

## C. Review checklist for returned designs

- Every screen shows its loading, empty, error and success states, plus any
  screen-specific states listed above.
- Status uses icon + text everywhere; both themes meet contrast.
- Graph screens (6, 7) include the table equivalent.
- P1 elements are visually separable from P0 ones.
- No external fonts, images or embeds; nothing renders user-supplied HTML.
- Deep-link states (/ci/{id}?tab=…, /impact?ci=…, /search?q=…, merged
  redirect, not found) are covered.
