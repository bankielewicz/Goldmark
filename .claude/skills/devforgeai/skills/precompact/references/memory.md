# Saving learnings to memory

Memory carries what a project's files don't: it outlives the handoff, which is about one piece of work. Save after
the handoff files are written, so a session cut off midway still leaves START-HERE.md.

## When there is a memory system

Use memory only when the session's own instructions describe one: where memories live and their format. Follow that
format exactly. For example, Claude Code's file-based memory keeps one fact per file, with a frontmatter of a name,
a one-line description and a type, plus one line per memory in an index file; it is written with the Write and Edit
tools, and the index line is added after the file. Another system may differ: its instructions decide.

## What to save

Save a learning that is durable and not recorded elsewhere:
- a decision with its reason, in the decider's words and with its date, when it outlives this piece of work;
- a verified fact about a tool, a platform or the environment (with how it was verified and when);
- a practice the user asked for or confirmed ("run the integration tests with the staging flag");
- a trap that cost time, with its fix.

Don't save:
- what the repository, its instructions or its history already record (code structure, a past fix, a commit);
- what matters only to this conversation or this piece of work: that belongs in START-HERE;
- a secret: no token, key or password, even one said in the conversation.

## How to save

1. Look for an existing memory that covers the same fact, by its index line or name. Update that one (Edit) rather
   than adding a duplicate; correct a memory this session proved wrong (Edit), and name in the report one that
   should be deleted, for the user to decide.
2. Write each new memory in the system's format, one fact each, with the date as YYYY-MM-DD, not "today".
3. Add or update its index line.
4. Name each saved or updated memory in the report.

A log, notes or papercuts file that the session's instructions ask to keep isn't a memory system: this skill
doesn't write it. Name it in the report, so the user can copy the learning there.

## When there is none, or a write is refused

When the session's instructions describe no memory system, or a memory write is refused (a read-only folder, a
denied tool), the learnings stay in START-HERE section 6, and the report says why none were saved.
