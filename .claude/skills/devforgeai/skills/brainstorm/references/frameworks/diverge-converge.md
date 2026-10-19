# Diverge-converge

## When to use / When not to use

**Use when** the topic is a new product, feature or problem space and the options are still wide
open. It is the default framework.

**Avoid when** the user already has a fixed shortlist and only wants it ranked. Then diverging
adds noise.

## Steps

1. **Frame the problems.** List who is hurting and how, from their side. Merge duplicates.
2. **Diverge.** Generate ideas without judging them: first the user's own ideas, in their words,
   then your own. Push for range: cheap and expensive, process and product, removing a step and
   adding one. Aim for 5 to 15 ideas. Link each to the problems it addresses.
3. **Surface assumptions.** For the ideas with the most promise, name what must be true for them
   to work, and how to check it.
4. **Evaluate.** Rate each idea's value, effort and risk, then compute the score (see the
   mapping below).
5. **Converge.** Propose a disposition for each idea from its score and the discussion:
   - promote the top ideas (usually 1 to 3);
   - park good ideas whose timing or effort is wrong;
   - reject ideas that fail on value or risk.

   Ask the user to confirm.

## Questions to ask

- Framing: "Who is affected, and what do they do today instead?"
- Framing: "What would make this problem go away for them?"
- Diverge: "Do you already have ideas? Tell me in your own words and I'll record them as given."
- Diverge: "What would you try with no budget limit? With no budget at all?"
- Assumptions: "What has to be true for this idea to work? How could we find out cheaply?"
- Converge: "Here are my proposed dispositions. Which do you confirm or change? Are we done?"

## Mapping to the BRN

| Step | Writes |
|---|---|
| Frame the problems | `problems`: `statement`, `who`, `evidence`, `severity` |
| Diverge | `ideas`: `idea`, `addresses`, with `disposition: open` and `reason: null` |
| Surface assumptions | `assumptions`: `statement`, `validation`, `state: open` |
| Evaluate | `ideas`: `value`, `effort`, `risk`, `score` |
| Converge | `ideas`: `disposition`, `reason`, only for user-confirmed values |

**Ratings.** Write `value`, `effort` and `risk` as the quoted strings `"high"`, `"medium"` or
`"low"`. `value` is the benefit to the users in the problems it addresses. `effort` is the cost
to build and run it. `risk` is the chance it fails or causes harm.

**Score.** Count high = 3, medium = 2, low = 1, and compute
`score = 2 × value − effort − risk`, an integer from −4 to 4. Write it unquoted. For example,
value high, effort low, risk medium gives 2 × 3 − 1 − 2 = 3. Leave all four fields `null` for an
idea you could not rate, and say why in section 5.

## Evaluation method text

Write this into section 5, followed by any discussion the numbers do not capture:

> Diverge-converge: problems were framed first, ideas were generated without judgment, and each
> idea was then rated high, medium or low for value, effort and risk. The score is
> 2 × value − effort − risk, counting high = 3, medium = 2 and low = 1 (range −4 to 4); a higher
> score is a stronger candidate for promotion.

## Example

Input: "Brainstorm how to cut appointment no-shows at our dental clinic."

Output (excerpt):

```yaml
ideas:
  - id: IDEA-01
    status: active
    idea: "Text a reminder two days before, with a one-tap reschedule link."
    addresses:
      - PRB-01
    value: "high"
    effort: "low"
    risk: "low"
    score: 4
    disposition: open
    reason: null
```

Section 6, before the user confirms:

> Proposed, awaiting confirmation: promote IDEA-01 (highest score, cheap to try); park IDEA-04
> (deposit policy needs a legal check); reject IDEA-06 (overbooking harms on-time patients).
