# Goldmark as a Windows service

Goldmark runs on Windows Server 2022 or 2025 as the service `Goldmark`, against SQL Server 2025 on the same
server or another one (slice 4b, DEC-043). A PowerShell wizard installs and upgrades it; Docker Compose is not
needed. This page covers the choices, the install, what it creates, upgrades, rotation, backups and the hand
check for a gMSA. The decisions, with the owner's words, are DEC-043.

## Before you start

- **An elevated PowerShell** (Windows PowerShell 5.1 or PowerShell 7) on the server, and the zip
  `goldmark-win-x64.zip` unpacked. The program is self-contained: the server needs no .NET install.
- **SQL Server 2025** (Express, Standard or Enterprise), reachable over TCP.
- **A TLS certificate on SQL Server that Goldmark can check.** Goldmark always encrypts (TDS 8 strict) and never
  turns certificate validation off. Either SQL Server's certificate is issued by a CA this server trusts, or you
  give the installer the certificate file to pin (`-SqlCertificatePath`). A fresh SQL Server has neither: its
  self-signed certificate can't be pinned, and Goldmark's first connection fails with "The certificate chain was
  issued by an authority that is not trusted". Give the instance a certificate first (SQL Server Configuration
  Manager, or your PKI).
- **The service account** (next section), and **CONTROL SERVER** (for example sysadmin) for whoever runs the
  installer, on every install and upgrade: `bootstrap-db` audits the instance before it changes anything
  (DEC-031, DEC-043 item 13). A DBA runs the installer, or grants it for the run.

## The service account

| Account | When | Why |
|---|---|---|
| A gMSA, `CORP\svc-goldmark$` (recommended) | A domain | Active Directory manages and rotates its password; nothing to store or leak. |
| A domain user, `CORP\svc-goldmark` | A domain without gMSAs | Its password is typed once and handed to Windows; Goldmark never stores it. |
| `NT SERVICE\Goldmark` (a virtual account) | SQL Server on the same server only | No password. On another server it would reach SQL Server as this computer's account. |
| LocalSystem, NetworkService | Never | They reach a remote SQL Server as the computer account `CORP\APP01$`, which every other such service on the server shares. |

A gMSA, with the ActiveDirectory module, by a domain administrator:

```powershell
New-ADServiceAccount -Name svc-goldmark -DNSHostName svc-goldmark.corp.example -PrincipalsAllowedToRetrieveManagedPassword APP01$
# On APP01:
Install-ADServiceAccount -Identity svc-goldmark
Test-ADServiceAccount -Identity svc-goldmark   # True
```

The installer grants the account "Log on as a service".

## Install

```powershell
Expand-Archive goldmark-win-x64.zip C:\Install\Goldmark
cd C:\Install\Goldmark
.\Install-Goldmark.ps1                                    # asks for what it needs
# or, without questions:
.\Install-Goldmark.ps1 -NonInteractive -SqlHost sql01.corp.example -ServiceAccount 'CORP\svc-goldmark$' `
    -Authentication Windows -AdminAuthentication Windows -HttpsPort 443
```

Every value can be a parameter; a missing one is asked for (or, with `-NonInteractive`, refused). On an
upgrade, the values in the existing settings file are the defaults.

| Parameter | Default | Prompt |
|---|---|---|
| `-SqlHost` | (none) | SQL Server host name |
| `-SqlPort` | 1433 | SQL Server TCP port |
| `-Database` | `Goldmark` | Database name |
| `-Authentication` | `Windows` | How the service signs in to SQL Server: Windows (recommended) or Sql |
| `-ServiceAccount` | `NT SERVICE\Goldmark` when SQL Server is on this server | Service account (a gMSA such as `CORP\svc-goldmark$`, or a domain user) |
| `-ServiceAccountPassword` | (a domain user only) | Password of the account (given to Windows only) |
| `-AdminAuthentication` | `Windows` | How you sign in to SQL Server to set up the database: Windows (as you) or Sql (an administrator login) |
| `-SqlAdminUser`, `-SqlAdminPassword` | (`-AdminAuthentication Sql` only) | SQL administrator login (CONTROL SERVER), its password |
| `-SqlCertificatePath` | (none: the CA must be trusted) | SQL Server certificate file to pin (copied to `settings\sql-server.crt`, where the service can read it) |
| `-SqlHostNameInCertificate` | (none) | Host name in the SQL Server certificate, if it differs |
| `-HttpsPort` | 443 | HTTPS port |
| `-CertificatePath`, `-KeyPath` | (none: a self-signed certificate is generated) | Your HTTPS certificate and its key (PEM, unencrypted key) |
| `-Hostnames` | this computer's names and `localhost` | Host names users type (for the generated certificate) |
| `-AcceptedFindings` | (none) | Accepted principal-audit findings (DEC-031) |
| `-AuditRetentionDays` | 365 | Days the audit log keeps entries (365 to 36500, or 0 for ever) |
| `-InstallDirectory` | `%ProgramFiles%\Goldmark` | |
| `-DataDirectory` | `%ProgramData%\Goldmark` | |
| `-NoStart` | | Install without starting |

What it does, in order:

1. Stops the service if it runs (an upgrade), copies the program, and creates or updates the service: start
   delayed-automatic, as the service account (never LocalSystem, not even for a moment), the environment
   variable `Goldmark__SettingsFile`, and restart after a crash (after a minute, twice, then not again that day).
2. Creates the data folders, each reachable by SYSTEM, Administrators and the service account only, with
   inheritance off: `settings` (the service reads it, never changes it), `keys`, `tls`, `credentials`, `logs`
   (the service may change these). Goldmark refuses to start if one of them is wider, and says which.
3. Writes `settings\goldmark.json`: the database, the service account, `Goldmark:Database:CheckRightsAtStart`,
   migrations as the administrator (`Goldmark:Migration:Login: Administrator`), the folders. No password is ever
   written to it, except the recovery code you may add (below).
4. Runs `bootstrap-db migrate` as you (or the SQL administrator you typed; that password stays in the command's
   environment only). Under Windows authentication it creates `CREATE LOGIN [CORP\svc-goldmark$] FROM WINDOWS`
   and its user in `goldmark_runtime`; there is no migration login.
5. Registers the Application log source `Goldmark`, opens the HTTPS port in the firewall, starts the service and
   waits for `/health/ready`.

## What runs where

- **The service** checks at each start, as its own login, that it holds exactly `goldmark_runtime`'s rights,
  rights it gets through Windows groups included; until then `/health/ready` fails. If it holds more, it stops
  and writes the reason to the Application log (event 3). Measured on 2026-10-22: an administrator impersonating
  the account (`EXECUTE AS LOGIN`) doesn't see rights from its Windows groups, so this check is the one that
  counts. A login for a Windows group the account is in (SQL Server Express creates `BUILTIN\Users`) is
  allowed; whatever that group is granted is checked like a grant to the account itself. Goldmark doesn't need
  `BUILTIN\Users`, and Microsoft says it can be safely removed to keep other local users out of the instance.
- **A setting that is wrong** stops the start; the setting's name (never its value) goes to the Application log
  (event 2). Windows doesn't restart a refused start.
- **The logs** are JSON, one file per day, in `logs\goldmark-YYYYMMDD.json`, kept 31 days
  (`Goldmark:Logging:RetainedFiles`). Nothing else goes to the Application log: anyone signed in on the server
  can read it (measured: `(A;;0x3;;;IU)`), and the log file carries the two secrets Goldmark ever logs, the setup
  token (event 3001) and the recovery link's path (3004).
- **Secrets at rest:** the sign-in keys (`keys`) and, with a SQL login, the runtime login's password
  (`credentials\runtime.json`) are encrypted to the service account: DPAPI-NG with a domain account, classic
  DPAPI with the virtual account. Outside a domain the installer can't encrypt to another account, so it writes
  the file in its owner-only folder and the service encrypts it to itself at its first start.

## First run

The setup token is in the log file (event 3001); the installer prints which file. Open
`https://<server>/setup`, as on Docker (US-02).

## Upgrade

Unpack the new zip and run `.\Install-Goldmark.ps1` again: it keeps the settings, stops the service, replaces the
program, migrates the database as you, and starts it.

## Rotating database passwords

Only a SQL login has one (`-Authentication Sql`); under Windows authentication there is nothing to rotate, and a
gMSA's password rotates itself.

```powershell
& 'C:\Install\Goldmark\Rotate-GoldmarkCredentials.ps1'   # asks for the SQL administrator's password if needed
```

It runs `rotate-db-credentials` (a new password, the file before the login, audited as System) and restarts the
service, which encrypts the new file to itself.

## Emergency Admin: the recovery setting

As on Docker (DEC-042 item 6), add to `settings\goldmark.json`, then `Restart-Service Goldmark`:

```json
"Auth": { "RecoverAdmin": "pat.admin", "RecoverAdminCode": "<12 to 64 letters and digits>" }
```

The link's path is in the log file (event 3004); the code is the one you wrote. Remove both afterwards and
restart: every start ends unused recovery links.

## Backup and restore

Back up, together: the database, `%ProgramData%\Goldmark` (`settings`, `keys`, `tls`, `credentials`), and keep
**the service account**. The sign-in keys are encrypted to it:

- restored under the same gMSA (DPAPI-NG), even on another server of the domain, they read (to check by hand,
  below);
- restored under another account, after changing the account (a domain user to a gMSA), or with the virtual
  account on a rebuilt server, they can't be read: everyone signs in with a recovery code and sets up two-factor
  again.

## Uninstall

```powershell
& 'C:\Install\Goldmark\Uninstall-Goldmark.ps1'              # keeps the data and the database
& 'C:\Install\Goldmark\Uninstall-Goldmark.ps1' -RemoveData  # deletes %ProgramData%\Goldmark too
```

The database is never dropped; a DBA does that.

## Checking a gMSA by hand

CI has no domain, so this is checked by hand on a domain-joined test server:

1. Install with `-ServiceAccount 'CORP\svc-goldmark$' -Authentication Windows`; `/health/ready` answers 200.
2. `Get-Content keys\key-*.xml` shows `<encryptedSecret>` with a `SID=` protection descriptor (DPAPI-NG).
3. Grant the gMSA's group a server permission in SQL Server; `Restart-Service Goldmark` stops with event 3
   naming it; revoke it.
4. Restore `%ProgramData%\Goldmark` to a second server where the same gMSA is installed; the service starts and
   people sign in with their authenticators (the keys read).

## When it doesn't start

- The service stops at once: the Application log, source `Goldmark`, event 2 (a setting) or 3 (its rights).
- `bootstrap-db` reports SQL Server error -2146893019 or 10054: SQL Server's certificate (see "Before you start").
- `bootstrap-db` refuses a `system-grant` finding: an edition or feature (Machine Learning Services, for
  example) granted `public` more than a fresh SQL Server 2025 does on Windows. Check it is the product's own
  grant, then run the installer again with `-AcceptedFindings <its ID>` (DEC-031, DEC-043 item 12).
- A folder "inherits its permissions" or "grants access to ...": restrict it, or remove it and run the installer
  again.
