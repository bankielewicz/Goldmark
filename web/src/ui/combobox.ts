// Every Combobox in the app passes this as its `expandIcon` (tests/combobox-expand-icon.test.mjs
// checks that): the arrow at the right of the field stays out of the Tab order.
//
// Fluent UI 9.75 (react-combobox 9.17.8) made that arrow a keyboard stop of its own, labelled "Open"
// in English only. Goldmark keeps the behaviour its screens were approved with (owner, 2026-11-18,
// DEC-040): Tab moves from field to field, the keyboard opens the list from the field itself (typing,
// or the arrow keys), and the arrow stays a pointer shortcut, as in the WAI-ARIA combobox pattern's
// examples.
//
// This does not make the arrow 24 px. It is the size it always was, under WCAG 2.2's 24 px target
// size (2.5.8). axe fails an undersized target only when Tab can reach it; out of the Tab order it
// reports "needs review", which the browser tests don't count, and before Fluent 9.75 it didn't look
// at the arrow at all. What makes the small arrow acceptable is 2.5.8's "equivalent" exception: a
// click anywhere in the field opens the same list, and the field is a full-size target.
export const comboboxExpandIcon = { tabIndex: -1 } as const;
