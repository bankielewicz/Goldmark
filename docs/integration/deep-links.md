# Deep links (US-53)

Add-ons and other tools can link into Goldmark's screens. These addresses are a public contract,
version 1: a later release only adds to them (new parameters, new tab values, new pages), and an
address that works now keeps working (US-54's rule for `/api/v1`, applied to the web app's addresses).
Slice 5 (DEC-044) ships the first two; the rest of US-53 follows in the slices named below.

| Address | Opens | Since |
|---|---|---|
| `/ci/{id}` | The CI's page (screen 5) | slice 5 |
| `/ci/{id}?tab={tab}` | The CI's page on that tab | slice 5 (`attributes`) |
| `/search?q={text}` | Search (screen 4) with CIs whose name contains the text | slice 5 |
| `/impact?ci={id}[&ci={id}…]` | Impact analysis for those CIs (screen 7) | slice 19 |

`{id}` is the CI's ID as the API returns it (`/api/v1/cis/{id}`): a UUID such as
`0192f3a4-7c1e-7d40-9a51-2b7e0c9d1f20`. It never changes, whatever happens to the CI's name.

## `/ci/{id}`

- `tab` is one of the CI page's tabs. Slice 5 has `attributes` (the default); slice 6 adds
  `relationships`, slice 7 `history`, slice 10 `identifiers` and slice 19 `impact`. A value the page
  doesn't know shows the Attributes tab and the address is corrected, so a link made for a later release
  still opens the CI.
- An ID no CI has, and text that isn't an ID, open a friendly "We can't find that CI" page with a search
  box, never an error.
- A CI merged into another one will redirect to the surviving CI and say so (slice 12, the review
  queue's merge and alias lookup). Until then a merge can't happen, so nothing redirects.
- A retired CI still opens, marked Retired.

## `/search`

The page address holds the whole view, so a search can be shared or bookmarked:

| Parameter | Meaning |
|---|---|
| `q` | Part of a CI's name, in any case. Identifiers (serial numbers, UUIDs, AD GUIDs) join with slice 10 |
| `type` | A CI type's name (`VirtualMachine`, `SqlInstance`, …); repeat it for several types |
| `retired=true` | Include retired CIs (hidden otherwise) |
| `sort` | `name` (the default), `-name`, `type`, `-type`, `changed` (oldest first) or `-changed` (newest first) |

`/search` with no parameters lists every CI that isn't retired, by name. An unknown `type` or `sort`
in a link is ignored by the page.

## Signing in

Every address needs a signed-in person. Someone who isn't signed in goes to the sign-in page and,
after signing in, straight back to the address they opened (`/sign-in?next=…`, same origin only).
Which CIs a person can open doesn't depend on their role: every role reads every CI; only Admins and
Editors change them.

## For add-on developers

- Build a CI link from the ID, never from the name: names need not be unique and can change.
- Encode `q` as a URL parameter (`encodeURIComponent`); it is matched as plain text, so `%`, `_` and
  `[` are only themselves.
- The same data is in the API: `GET /api/v1/cis?q=…&type=…&retired=true&sort=…` and
  `GET /api/v1/cis/{id}` (`docs/security/endpoint-access.md`). API tokens for scripts arrive in slice 8.
