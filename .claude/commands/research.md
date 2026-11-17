---
description: Goldmark architecture council research on a question
argument-hint: [question or decision]
---

<!--
Usage: /research [question or decision]
Example: /research Should the graph's relationship table be paged on the server?
With no question, runs the workflow's default review.
Workflow: devforgeai/prompts/goldmark-cmdb-architecture-council.md (the single source; edit it there)
-->

# /research - Goldmark architecture council

Run the Goldmark CMDB architecture council workflow below in this session.

@devforgeai/prompts/goldmark-cmdb-architecture-council.md

If the workflow's text is not visible above, read `devforgeai/prompts/goldmark-cmdb-architecture-council.md` in full before doing anything else. If the file is missing, say so and stop.

## Session input

**Question or decision:** $ARGUMENTS

If the line above is empty, run the workflow's default review: identify the three most valuable improvements supported by the current implementation and the approved product goals, covering usability, performance, trust in results and feature completeness.

## How to run it

- The workflow is your instructions for this turn. Its opening line about copying the file into an LLM session does not apply: follow it here, and do not hand it to another model or print it back.
- Follow every section: authority and baseline, specialist perspectives, research method, the analysis and recommendation contract, and the required response format. Revalidate the starting snapshot (branch, commit, working tree, date) before relying on it.
- Specialists: choose only the perspectives the question needs and run them as subagents with the Agent tool, at most four at once, each read-only and briefed as the workflow describes. Check their citations and code claims before using them. If you do not run subagents, apply the perspectives in turn and label the result a single-model review.
- Research on the web as the workflow's research method requires. Open each source you cite. Put no private repository content, estate identifiers, secrets or internal artifacts into a search query.
- Read-only: change no files in the repository, commit nothing, publish nothing and send no external messages. Implementation needs the owner's separate authorization. Running existing tests and throwaway measurements (scripts and output in the scratchpad, not the repository) is allowed and is not implementation.
- Bring decisions that are the owner's to make back in the workflow's decision register, with lettered options and a recommendation.

## Pros, cons and the question to the owner

This section adds to the workflow's response format and applies every time you present the response, whether to the owner or as an answer for another session.

1. **Pros and cons.** For each recommendation, and for each option in the decision register, list the pros and the cons beside it: what the choice gains, what it costs, and what it rules out. Keep each to a line and tie it to evidence (a code path, a decision ID, a measurement, a source).
2. **Risk against reward, calculated.** For each decision, work out what choosing one option over the others costs and gains in terms that can be counted: lines and files touched, pushes and CI or smoke runs, time, rows or users affected, a contract or decision broken, work a later slice must redo. Don't estimate what you can find out: if you are unsure, or the answer needs a measurement, run the existing test or a throwaway measurement first and report the result. Say which figures are measured, which are traced in source and which are assumed.
3. **Ask when a wrong pick is severe.** A consequence is severe when picking one option over the others would break an approved decision or contract, lose or corrupt data, weaken security, be costly or impossible to reverse, or leave technical debt. For each decision with a severe consequence, ask the owner with AskUserQuestion in the same turn as the response: one question per decision, the recommended option first and marked "(Recommended)", each option's description giving its consequence and its risk against its reward. Put every such decision in one AskUserQuestion round. A decision with no severe consequence is not asked: state the recommendation and its reason in the response.
4. **When the response goes to another session.** If the question came from another session, or the owner will relay the answer to one, also write the response so it can be pasted there, and put each open question in it with its options, pros and cons, and recommendation. The other session may answer a question in the owner's absence only when its answer introduces no technical debt in the project being worked on; say that in the pasted text. If any option could introduce technical debt, mark the question "owner only" and tell the other session to hold it for the owner.
5. Report the owner's answers, and any left unanswered, in the decision register.
