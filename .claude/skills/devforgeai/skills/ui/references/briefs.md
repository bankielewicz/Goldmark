# Flows and briefs

## Contents

- When to use this
- Listing the screens and grouping them into flows
- The brief's shape
- The guidance behind it
- A worked example: a terminal flow
- A worked example: a web flow
- Self-check list for a brief
- When the grouping or the briefs are not confirmed (ERR-22)
- When nothing names a screen (ERR-24)
- Without the Artifact tool (ERR-19)

## When to use this

Read this at step 3 of SKILL.md, before listing the screens. It covers what the skill proposes before any
canvas exists: the grouping of screens into flows, and one brief for each confirmed flow. A brief is a
decision that leaves the repository, so the user confirms the grouping and the briefs first. Nothing is sent
before the question at step 4.

## Listing the screens and grouping them into flows

1. List the screens that the BRN's promoted ideas name. An idea names a screen when it names a screen, a flow or
   a user interface, a command line included; an idea the request names (`IDEA-NN`) counts as naming one. Read
   the BRN's problems and assumptions too, for the real content of a brief. When no idea names a screen, see
   "When nothing names a screen" below.
2. Propose how the screens group into flows. Give each flow a lowercase slug (`day-to-day`), its screens in
   order, its **key screen** (the one that carries the flow) and its **surface**: `web`, `desktop`, `mobile` or
   `terminal`. A flow takes the surface of its key screen; when its screens differ in surface, propose splitting
   the flow, and the user may.
3. The number of screens is not known up front. The BRN gives the first count; PRD requirements and ADR
   consequences add more later, through an amend run. Never invent a screen the ideas do not name.
4. The skill never decides the flows. Ask, with AskUserQuestion when it is available: "Group the screens like
   this?" with "Confirm the grouping" (Recommended), "Change the grouping" and "One flow for the whole release".
   A changed grouping is proposed again. A single confirmed flow gives one brief for the whole release.
5. At most 4 flows are drawn in one pass, with 3 boards for each (three directions of the key screen). With more
   confirmed flows, the first four, in the order the user confirms them, are drawn; the rest come through the
   offer to add a flow in a later amend run.

## The brief's shape

A brief is plain text, shown to the user and sent to the Design type. It is not a file, and no script checks it.
It has six parts, in this order:

| Part | Holds |
|---|---|
| 1. Lead line | One line naming the screen or flow, the product, and who it is for |
| 2. Context | Who uses it and the one job it does: two or three sentences |
| 3. Content | Real data and copy, quoted from the BRN's promoted ideas, problems and assumptions, never invented; the flow's screens, in order, with the key screen marked; the states to show (empty, error, loading, mid-flow) that the BRN supports |
| 4. Must-haves | Two to four hard constraints: the surface and its size. For a terminal surface: a monospace cell grid of stated columns by rows, drawn only with text, box-drawing and block characters, 24-bit colour, keyboard-driven (propose a size such as 120 columns by 40 rows; the user confirms it with the brief) |
| 5. Style | By reference: the user's default design system, named as the Artifact tool's `quickstart` lists it, else the words "propose one". Never a restatement of tokens, colours or hex values |
| 6. Closing line | The exact line `Give me 3 distinctly different directions of the key screen first, with a one-line tradeoff under each.` |

## The guidance behind it

These are Anthropic's rules for prompting Claude Design, applied.
- A brief describes the problem and never prescribes the solution: no positions, spacing, sizes of parts or
  component-by-component layout. Claude Design finds the layout; the user iterates afterwards.
- A brief covers one flow. Its Content lists the flow's screens in order, but its closing line asks for three
  directions of the key screen first and not of every screen: the user picks a direction and asks Claude Design
  to carry it across the flow's other screens.
- Do not ask for every screen and every state at once.
- The style is a reference to the design system that exists, so there is one owner of the look (the context
  documents). The skill reads the user's design systems with `quickstart`, which changes nothing.
- Real content makes the directions comparable: quote the BRN's wording, and leave out what the BRN does not
  support.
- A brief holds no HTML comment.

## A worked example: a terminal flow

The example BRN is "Shiftlog: record shifts". Its ideas include IDEA-01 "Add a shift from the terminal" and
IDEA-02 "List shifts in a table"; its problem PRB-01 reads "Shifts are written on paper and get lost". The two
screens form the flow `shifts`, key screen List, surface `terminal`:

```
Shifts in the terminal: the List and Add screens of Shiftlog, a terminal tool, for people who work shifts and record each one from the command line.

Context: Shiftlog is used by people who work shifts and today write them on paper, where they get lost. The one job of this flow: record a shift and see the shifts already recorded at a glance.

Content: the idea "List shifts in a table"; the idea "Add a shift from the terminal"; the problem "Shifts are written on paper and get lost". The flow's screens, in order: 1. List (the key screen), 2. Add. States to show: no shift recorded yet; a list of recorded shifts; an error when a shift cannot be saved.

Must-haves: a terminal screen, a monospace cell grid of 120 columns by 40 rows; drawn only with text, box-drawing and block characters and 24-bit colour; a dark terminal; fully keyboard-driven.

Style: propose one.

Give me 3 distinctly different directions of the key screen first, with a one-line tradeoff under each.
```

## A worked example: a web flow

The same BRN has IDEA-03 "A weekly report page" and IDEA-06 "A dark theme for the report page". They form the flow
`report-and-home`, key screen Report, surface `web`:

```
The weekly report: the Home and Report pages of Shiftlog, a web app, for people who record their shifts and want to see a week at a glance.

Context: Shiftlog users record shifts and then want to know how much they worked. The one job of this flow: open the app and read the week's report.

Content: the idea "A weekly report page"; the idea "A dark theme for the report page". The flow's screens, in order: 1. Home, 2. Report (the key screen). States to show: a week with no shifts; a week with shifts; the report while it loads.

Must-haves: a web page for a desktop browser, 1280 pixels wide; readable in a dark theme; usable with the keyboard alone.

Style: propose one.

Give me 3 distinctly different directions of the key screen first, with a one-line tradeoff under each.
```

When `quickstart` lists a default design system, the Style line names it ("Style: follow the design system
Aurora" for a system called Aurora) and still restates none of its values.

## Self-check list for a brief

Read each brief against this before showing it:
1. It has six parts, in order; the lead line is one line naming the flow, the product and who it is for.
2. The Context is two or three sentences and names one job.
3. The Content quotes the BRN's wording, lists the flow's screens in order with the key screen marked, and names
   only states the BRN supports. Nothing is invented.
4. The Must-haves are two to four items and state the surface and its size; a terminal flow states a cell grid
   of columns by rows.
5. The Style is a reference ("propose one" or a named design system) with no token, colour, hex value or size.
6. No position, spacing, size of a part or component-by-component layout appears anywhere.
7. It covers one flow, and asks for every screen and state at once nowhere.
8. The closing line is exact, and the brief holds no HTML comment.

## When the grouping or the briefs are not confirmed (ERR-22)

When the user declines, asks for another grouping or a change, or no answer can arrive (a non-interactive run, or
"proceed without questions"): make no canvas and send nothing.
- A changed grouping: propose it again and redraft the briefs.
- A changed brief: redraft it and ask again.
- Otherwise show the grouping and the briefs, say how to resume (run the skill again), and write nothing.

## When nothing names a screen (ERR-24)

When no promoted idea names a screen, a flow or a user interface, the request names no idea to draw and no canvas
URL, and the boards folder holds no copy to record: write nothing. Say that nothing in the brainstorm asks for a
screen, that this step is optional, and that the user can name the ideas to draw or go on with
`/devforgeai:prd BRN-NNN`.

## Without the Artifact tool (ERR-19)

Check that the Artifact tool is available before asking anything (SKILL.md, Tools). Without it, list the screens
and show the proposed grouping and the briefs for it, unconfirmed, ask nothing, and say that they can be pasted
into `/design` by hand. Then apply ERR-19 as SKILL.md says. Never draw a mockup of a screen, in a reply or in
the terminal, in place of the canvas.
