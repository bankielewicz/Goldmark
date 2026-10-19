# Using an existing SQL Server

Goldmark can use a SQL Server 2025 instance you already run instead of the bundled
SQL Server Express container (US-01). Use `deploy/docker-compose.external-sql.yml`
in place of `deploy/docker-compose.yml`; both read `deploy/.env`. Decisions:
DEC-031 (principal audit), DEC-032 (administrator login, modes), DEC-033 (Compose
file).

```sh
cp deploy/.env.example deploy/.env && chmod 600 deploy/.env
# fill in GOLDMARK_DB_PASSWORD, GOLDMARK_DB_MIGRATION_PASSWORD and the
# "Existing SQL Server" section (host, administrator login, TLS)
docker compose -f deploy/docker-compose.external-sql.yml up -d --build --wait
```

The one-shot `db-init` container runs `bootstrap-db migrate`, then the app starts.
`db-init` runs on every `docker compose up`, so the administrator credential stays in
`deploy/.env` (mode `600`). Only `db-init` receives it; the app container gets the
runtime login only.

## Requirements

- **SQL Server 2025** (Express, Standard or Enterprise; Linux or Windows). Goldmark
  uses the native `json` type.
- **TCP** reachable from the Docker host, SQL Server authentication enabled (mixed
  mode). Windows authentication is not supported yet.
- **Encryption.** `GOLDMARK_DB_ENCRYPT=Strict` (TDS 8, default) or `Mandatory`
  (TLS-encrypted TDS 7.x). The server certificate is always validated:
  - pin it with `GOLDMARK_SQL_SERVER_CERT_FILE` (an absolute host path, or `./…`
    relative to `deploy/`): the right choice for a self-signed or internal-CA
    certificate;
  - or leave that empty if the certificate chains to a public CA;
  - set `GOLDMARK_DB_HOST_NAME_IN_CERTIFICATE` when the certificate's name differs
    from `GOLDMARK_DB_HOST`.

  `TrustServerCertificate` is never used.
- **One standalone instance.** Availability groups are not supported yet:
  `bootstrap-db` creates logins on the instance it connects to, and logins are not
  copied to other replicas.

## The administrator login

`bootstrap-db` connects with `GOLDMARK_DB_ADMIN_USER`. It needs **CONTROL SERVER**
(for example membership of `sysadmin`), because it:

- reads catalogs in every database and the instance's linked-server and credential
  mappings (the principal audit);
- compares the configured passwords with the existing logins (`PWDCOMPARE` reads
  password hashes);
- in Mode `Create`, creates the database and both logins.

It refuses (exit 3) an administrator without CONTROL SERVER before doing anything.
**`sa` is refused** (exit 2): it is the first name attackers try. Create a dedicated
login with a long random password, for example:

```sql
CREATE LOGIN goldmark_dba WITH PASSWORD = N'<long random password>', CHECK_POLICY = ON;
ALTER SERVER ROLE sysadmin ADD MEMBER goldmark_dba;
```

## Mode Create and Mode Existing

`GOLDMARK_DB_BOOTSTRAP_MODE` decides whether `bootstrap-db` may create anything.

- **`Create` (default).** It creates the database (with `AUTO_CLOSE OFF`), the runtime
  and migration logins, their users and the migration login's `db_owner` membership,
  as needed, and rotates their passwords when you change them in `deploy/.env`.
  Existing logins or users it did not create are refused unless
  `GOLDMARK_DB_ADOPT_EXISTING_LOGIN=true`.
- **`Existing`.** A DBA creates everything first; `bootstrap-db` creates and changes
  nothing except Goldmark's marker (the `goldmark:managed` extended property) on the
  two database users. It refuses, with nothing changed, if the database, a login, a
  user or the `db_owner` membership is missing, if a user's name differs from its
  login's, or if a password differs from `deploy/.env`. It never changes a password.

The DBA script for Mode `Existing` (use your names; the passwords must match
`GOLDMARK_DB_PASSWORD` and `GOLDMARK_DB_MIGRATION_PASSWORD`):

```sql
CREATE DATABASE [Goldmark];
ALTER DATABASE [Goldmark] SET AUTO_CLOSE OFF;
CREATE LOGIN [goldmark_app] WITH PASSWORD = N'<runtime password>', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
CREATE LOGIN [goldmark_migrator] WITH PASSWORD = N'<migration password>', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
USE [Goldmark];
CREATE USER [goldmark_app] FOR LOGIN [goldmark_app];
CREATE USER [goldmark_migrator] FOR LOGIN [goldmark_migrator];
ALTER ROLE db_owner ADD MEMBER [goldmark_migrator];
```

Grant nothing else: `migrate` adds the runtime user to the `goldmark_runtime` role,
which the migrations create, and refuses any other right.

## What `db-init` checks on every run

1. The administrator has CONTROL SERVER; in Mode `Existing`, the database exists.
2. Each login is a SQL login, its database user has the login's name, it is not
   managed by Goldmark for the other purpose, and (Mode `Existing`) everything above
   is in place.
3. **The principal audit, before any change** (below). Any finding refuses with
   nothing changed.
4. Provisioning (Mode `Create`) or only the marker (Mode `Existing`).
5. The audit again, for principals this run created.
6. As each login: its effective roles and permissions (DEC-015, DEC-024) and the
   audit's effective-rights layer.
7. `migrate`, as the migration login: the same checks for both logins (without the
   pass over other databases), the migrations, and the runtime login's exact rights
   (DEC-025).

Exit codes: 0 ok, 1 SQL Server or connection error, 2 invalid configuration,
3 refused.

## The principal audit

The audit (issues #2, #3, #4) looks for rights Goldmark's logins hold, or would
inherit, beyond what Goldmark grants them. It runs in both Compose files, on every
run. It has two layers:

- **the administrator's view:** catalogs, linked servers, ownership, login state and
  grants to `public`/`guest`;
- **each login's own view:** its effective rights, so rights that arrive through a
  role, a group or `guest` are found too.

Every finding has an **ID** (`kind:part[:part…]`, names encoded so an ID never
contains a comma, colon or space) and a fix. For example:

```text
bootstrap-db: refusing to continue: the principal audit found 1 problem(s):
  - [guest-connect:Sales] guest can connect to database 'Sales', so every login, Goldmark's included, can enter it. Fix: USE [Sales]; REVOKE CONNECT FROM guest; On a shared server you may instead accept it: add guest-connect:Sales to GOLDMARK_DB_ACCEPTED_FINDINGS.
Nothing was changed.
```

### Accepting findings on a shared server

Some findings are about other databases or the instance, which you may not control
on a shared server. You may accept those **by ID**:

```sh
GOLDMARK_DB_ACCEPTED_FINDINGS=guest-connect:Sales,system-grant:msdb:public:SELECT:dbo.sysjobs
```

- An accepted finding is still reported on every run (`accepted finding [...]`).
- An accepted ID that no longer occurs is a warning, so the list does not go stale.
- A malformed ID, or the ID of a finding that cannot be accepted, is exit 2.
- There is no "accept everything" setting.

### Finding kinds

Acceptable kinds (about the shared instance):

| Kind | Meaning |
|---|---|
| `guest-connect:<db>` | `guest` has `CONNECT` in another database (default only in master, tempdb, msdb), so every login can enter it (#2) |
| `linked-login-all-logins:<server>` | A linked server maps every login to a fixed remote login (#3). The default self-credential mapping is not reported |
| `remote-login-all-logins:<server>` | A remote-login mapping for every login (#3) |
| `system-grant:<db>:<grantee>:<permission>[:<target>]` | A grant to `public` or `guest` in master, msdb, tempdb or model beyond SQL Server 2025's own (#4; measured baseline in `src/Goldmark.Api/Commands/Audit/`) |
| `cross-db-chaining` | The server option `cross db ownership chaining` is on |
| `database-unreadable:<db>:<error>` | A database could not be read (for example a non-readable secondary), so it was not checked |

Kinds that must be fixed (about Goldmark's logins or database):

| Kind | Meaning |
|---|---|
| `goldmark-grant:<db>:<grantee>:<permission>[:<target>]` | A grant to `public` or `guest` in the Goldmark database beyond a new database's default (a grant in `model` is copied into new databases) |
| `goldmark-database-option:<db>:<option>` | `TRUSTWORTHY` or `DB_CHAINING` is on for the Goldmark database |
| `public-server-grant:<permission>:<class>[:<target>]` | A server-level grant to `public`: every login, Goldmark's included, holds it |
| `login-disabled`, `login-policy-off`, `login-expiration-on`, `login-expired`, `login-locked`, `login-must-change` `:<login>` | Login state that weakens the password or makes Goldmark stop working on its own |
| `login-connect-denied:<login>:<grantee>` | `CONNECT SQL` is denied |
| `login-credential:<login>:<credential>` | The login is mapped to a credential |
| `server-role:<login>:<role>`, `server-grant:<login>:<permission>:<class>[:<target>]` | Server-level rights held directly |
| `owns:<login>:<kind>:<name>` | The login owns a database, a schema, an object or another securable (everything Goldmark uses is owned by `dbo`, DEC-029) |
| `linked-login:<login>:<server>`, `remote-login:<login>:<server>` | A mapping for this login (#3) |
| `mapped-into:<login>:<db>`, `database-access:<login>:<db>:<user>` | The login has a user in another database (directly or through a group) |
| `database-role:<login>:<role>`, `database-grant:<login>:<permission>:<class>[:<target>]` | In the Goldmark database, a role or a direct grant besides the expected ones (rights come only through `goldmark_runtime`, or `db_owner` for the migration login) |
| `database-connect-denied:<login>:<grantee>` | `CONNECT` on the Goldmark database is denied |
| `server-securable:<login>:<class>:<name>:<permission>` | A right on another login, server role or endpoint (for example `IMPERSONATE`) |
| `database-securable:<login>:<class>:<name>:<permission>` | For the runtime login, a right on anything in the Goldmark database besides DML on schema `cmdb` and `SELECT` on the migration journal |

## Changing passwords

In Mode `Create`, change `GOLDMARK_DB_PASSWORD` or `GOLDMARK_DB_MIGRATION_PASSWORD`
and run `docker compose … up -d`: `db-init` rotates the login's password. In Mode
`Existing`, the DBA changes the password first, then you update `deploy/.env`.

## Not supported yet

- Windows authentication and group logins (planned with the Windows service
  deployment).
- Availability groups and other multi-instance setups.
- Running without the administrator login after installation.
