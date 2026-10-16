# Security policy

Goldmark is a CMDB: a map of an organization's infrastructure. Treat any
vulnerability in it as sensitive.

## Status

Goldmark is under development. There is no released version yet, so there are
no supported versions. This section will list supported releases once the
first one ships.

## Reporting a vulnerability

**Do not open a public issue, pull request or discussion for a security problem.**

Report vulnerabilities privately through GitHub's private vulnerability
reporting: on the repository page, open *Security → Report a vulnerability*.
If that option is not available, contact the maintainer,
[@bankielewicz](https://github.com/bankielewicz), privately through GitHub
and ask for a private channel. Do not include details in public places.

Please include:

- the affected component (API, web UI, collectors, deployment files) and commit or version;
- steps to reproduce, or a proof of concept;
- the impact as you understand it;
- whether the issue is already public or being exploited.

What to expect:

- an acknowledgement, then an assessment of severity and impact;
- a fix developed privately, with credit to you in the advisory unless you prefer otherwise;
- coordinated disclosure once a fix is available.

Please give us a reasonable time to fix the issue before disclosing it, and do
not access data that is not yours, degrade service for others, or test against
systems you do not own.

## Scope notes

- Collector credentials stay on collector hosts; the server stores none (PRD FR-CO-2).
  Reports showing otherwise are in scope.
- Findings that require an already-compromised Docker host or SQL Server `sa`
  account are generally out of scope, unless Goldmark makes them worse.
