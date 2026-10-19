> Part of CTX-NNN (<document>.md), version N. A change here raises that document's version.

# <Document title>: <topic>

<!-- A detail file of a project context document. It holds content that only some work needs, or that
     would push its parent past 500 lines. It lives in docs/specs/context/<document>/<topic>.md and is
     linked from its parent with one line saying when to read it.
     - It has no frontmatter and no ID of its own: it is versioned with its parent, and any change to it
       raises the parent's `version` and adds a row to the parent's Change Log.
     - It holds prose and tables only. The parent's item blocks (tech-stack.md's technologies,
       source-tree.md's roots) never move here (a rule of these templates).
     - It may link its parent (../<document>.md), other context documents (../<name>.md) and files
       outside docs/specs/context/. It never links a detail file, its own parent's or another's.
     - Statements use the parent's kinds. A Decision's source has its link in the parent's frontmatter.
       Markers and active Proposed statements here block the parent's approval.
     - At most 500 lines; past 100 lines, start with a "## Contents" list.
     Delete these comments when you fill in the file. -->

## <Section>

- **Convention:** <…>
