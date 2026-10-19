import { expect, test } from 'claude-code/testing'
import {
  adherenceText, bandRows, editResult, eventLine, finalTimeout, exitOf, fit, followsTaskList, hasTaskList, isAnswered,
  isEngine, isFailed, isPersonPrompt, isTracked, keptContent, newFlagToasts, questionRefusal, refusalText, relPath,
  refusalCause, replyText, reportContext, retentionOf, runId, skillName, statusText, stepOfTask, stepStateOf, stuckAdvice,
  stuckText, summaryOf, taskIdOf, returnLine, trailNote, pausedWith, keptTrail, endReason, hasRoom, nestedExitQuestion, nestedKeptText,
  runName, resumePlan, ageText, resumeQuestion,
  todoSteps, toolPath, CONTENT_LIMIT, LOG_CONTENT_LIMIT, QUESTION_REFUSAL, QUESTION_TAG,
} from './progress-core'
import type { ProgressState } from './progress-core'

// Pure helpers of the adapter (SPEC-013): the event line's bytes, the evidence rules, the display text.

const T0 = Date.UTC(2026, 9, 2, 12, 0, 0)
const RUN = '20261002T120000Z-brainstorm-0a1b2c3d'

test('an event line puts run, seq, time and kind first, then the kind fields in DM-02 order', () => {
  const line = eventLine(RUN, 3, T0 + 3000, 'tool', {
    content: 'x', error: false, exit: 0, command: undefined, path: 'docs/a.md', tool: 'Write', extra: 1,
  })
  expect(line).toBe('{"run":"20261002T120000Z-brainstorm-0a1b2c3d","seq":3,"time":"2026-10-02T12:00:03Z",'
    + '"kind":"tool","tool":"Write","path":"docs/a.md","exit":0,"error":false,"content":"x"}')
  expect(eventLine(RUN, 1, T0, 'skill-loaded', {
    modeSource: 'framework-default', mode: 'observe', host: 'claude-code 2.1.287', checklist: 'c', skill: 'brainstorm',
    format: 'devforgeai-events/1',
  })).toBe('{"run":"' + RUN + '","seq":1,"time":"2026-10-02T12:00:00Z","kind":"skill-loaded",'
    + '"format":"devforgeai-events/1","skill":"brainstorm","checklist":"c","host":"claude-code 2.1.287",'
    + '"mode":"observe","modeSource":"framework-default"}')
  expect(eventLine(RUN, 9, T0, 'prompt', { text: 'not a prompt field' })).toBe(
    '{"run":"' + RUN + '","seq":9,"time":"2026-10-02T12:00:00Z","kind":"prompt"}')
  expect(eventLine(RUN, 4, T0, 'tool', { tool: 'Bash', exit: null, error: true })).toContain('"exit":null,"error":true')
})

test('a run ID is the UTC time, the skill and 8 hex digits, matching SPEC-012', () => {
  const id = runId(T0, 'brainstorm', new Uint8Array([10, 27, 44, 61, 99]))
  expect(id).toBe('20261002T120000Z-brainstorm-0a1b2c3d')
  expect(runId(T0, 'My_Skill', new Uint8Array([0, 0, 0, 1]))).toBe('20261002T120000Z-my-skill-00000001')
  expect(/^[0-9]{8}T[0-9]{6}Z-[a-z][a-z0-9-]*-[0-9a-f]{8}$/.test(runId(T0, '9lives', new Uint8Array(4)))).toBe(true)
})

test('skill names lose the plugin prefix, and tracking takes plugin skills and manifest names', () => {
  expect(skillName('devforgeai:brainstorm')).toBe('brainstorm')
  expect(skillName('brainstorm')).toBe('brainstorm')
  expect(isTracked('git', ['brainstorm', 'git'], [])).toBe(true)
  expect(isTracked('release', ['brainstorm'], ['release'])).toBe(true)
  expect(isTracked('plugin-dev', ['brainstorm'], ['release'])).toBe(false)
})

test('paths are relative to the root with / separators; outside the root they stay absolute', () => {
  expect(relPath('/work', '/work/docs/specs/a.md')).toBe('docs/specs/a.md')
  expect(relPath('/work/', '/work/docs')).toBe('docs')
  expect(relPath('/work', '/elsewhere/a.md')).toBe('/elsewhere/a.md')
  expect(relPath('/work', '/work')).toBe('.')
  expect(toolPath('/work', 'Read', { file_path: '/work/docs/specs/prd/PRD-001.md' })).toBe('docs/specs/prd/PRD-001.md')
  expect(toolPath('/work', 'Glob', { pattern: 'BRN-*.md', path: '/work/docs/specs/brainstorm' }))
    .toBe('docs/specs/brainstorm/BRN-*.md')
  expect(toolPath('/work', 'Glob', { pattern: 'docs/**/*.md' })).toBe('docs/**/*.md')
  expect(toolPath('/work', 'Grep', { pattern: 'x', path: '/work/docs/specs/arch/' })).toBe('docs/specs/arch/')
  expect(toolPath('/work', 'Grep', { pattern: 'x' })).toBe('.')
  expect(toolPath('/work', 'Bash', { command: 'ls' })).toBe(undefined)
})

test('a refused or failed call is error true and never ran as asked; exit comes from the text', () => {
  expect(isFailed({ deny: 'refused by a hook' })).toBe(true)
  expect(isFailed({ isError: true, text: 'Exit code 3' })).toBe(true)
  expect(isFailed({ result: 'ok', text: 'ok' })).toBe(false)
  expect(exitOf({ result: 'ok', text: 'ok' })).toBe(0)
  expect(exitOf({ isError: true, result: 'Error: Exit code 3', text: 'Exit code 3' })).toBe(3)
  expect(exitOf({ isError: true, text: 'Permission denied' })).toBe(null)
  expect(exitOf({ deny: 'no' })).toBe(null)
})

test('an AskUserQuestion is answered only when it did not fail and holds an answer', () => {
  expect(isAnswered({ result: { questions: [], answers: { 'Q?': 'Yes' }, annotations: {} } })).toBe(true)
  expect(isAnswered({ result: { questions: [], answers: { 'Q?': 'not sure' } } })).toBe(true)
  expect(isAnswered({ isError: true, result: "Error: The user doesn't want to proceed with this tool use." })).toBe(false)
  expect(isAnswered({ result: { questions: [], answers: {} } })).toBe(false)
  expect(isAnswered({ deny: 'refused' })).toBe(false)
})

test('an Edit leaves the file with one replacement, or all with replace_all; otherwise none', () => {
  expect(editResult('a b a', 'b', 'c', false)).toBe('a c a')
  expect(editResult('a b a', 'a', 'z', true)).toBe('z b z')
  expect(editResult('a b a', 'a', 'z', false)).toBe(null)
  expect(editResult('a b a', 'q', 'z', false)).toBe(null)
  expect(editResult('x'.repeat(CONTENT_LIMIT + 1), 'x', 'y', true)).toBe(null)
})

test('content is kept up to 64 KiB, and not once the log passes 3 MiB', () => {
  expect(keptContent('short', 0)).toBe('short')
  expect(keptContent('x'.repeat(CONTENT_LIMIT + 1), 0)).toBe(undefined)
  expect(keptContent('short', LOG_CONTENT_LIMIT)).toBe(undefined)
  expect(keptContent(null, 0)).toBe(undefined)
})

test('a response row gives its text blocks, and nothing for a tool call or thinking', () => {
  expect(replyText([{ type: 'text', text: '- [x] 1. probe tick' }])).toBe('- [x] 1. probe tick')
  expect(replyText([{ type: 'tool_use', id: 't', name: 'Bash', input: {} }])).toBe('')
  expect(replyText([{ type: 'thinking', thinking: '…' }])).toBe('')
  expect(replyText([{ type: 'text', text: 'a' }, { type: 'text', text: 'b' }])).toBe('a\nb')
  expect(replyText('plain')).toBe('plain')
})

test('prompts count from the person, answers from Claude Code', () => {
  expect(isPersonPrompt({ kind: 'composer' })).toBe(true)
  expect(isPersonPrompt({ kind: 'bridge' })).toBe(true)
  expect(isPersonPrompt({ kind: 'task-notification' })).toBe(false)
  expect(isPersonPrompt({ kind: 'plugin', name: 'x' })).toBe(false)
  expect(isPersonPrompt(undefined)).toBe(false)
  expect(isEngine({ plugin: 'engine', tier: 'core' })).toBe(true)
  expect(isEngine({ plugin: 'other-mod', tier: 'user' })).toBe(false)
})

const STATE: ProgressState = {
  skill: 'brainstorm',
  current: 6,
  ended: null,
  steps: [1, 2, 3, 4, 5, 6, 7, 8].map(n => ({
    n, title: `Step ${n}`,
    state: n < 5 ? 'done' : n === 5 ? 'skipped' : n === 6 ? 'current' : 'pending',
  })),
  flags: [{ gate: 'write', seq: 4, step: 5, type: 'skipped', message: 'step 5 had no answer' }],
  gate: { kind: 'write', seq: 4, refuse: true, reason: 'step 5 had no answer' },
  manifest: { state: 'matched' },
}

test('the status text reads as BEH-10 says', () => {
  const s = summaryOf(STATE)
  expect(statusText(s, 'observe', false, null)).toBe('brainstorm 6/8 · 1 flag')
  expect(statusText({ ...s, flags: 2 }, 'enforce', false, null)).toBe('brainstorm 6/8 · 2 flags · enforce')
  expect(statusText({ ...s, flags: 0, yourTurn: true, skill: 'architecture', current: 7, steps: 11 }, 'observe', false, null))
    .toBe('architecture 7/11 · your turn')
  expect(statusText({ ...s, current: null, flags: 0 }, 'observe', false, null)).toBe('brainstorm done')
  expect(statusText({ ...s, ended: 'clear', flags: 0 }, 'observe', true, null)).toBe('brainstorm ended')
  expect(statusText({ ...s, flags: 0, manifest: 'none', skill: 'git' }, 'observe', true, null)).toBe('git 6/8 · ticks only · idle')
  expect(statusText(s, 'observe', false, 'python not found')).toBe('progress: off (python not found)')
  expect(statusText(null, 'observe', false, null)).toBe(undefined)
})

test('the band rows carry the glyphs, the step, the mode and its button, and the newest flag', () => {
  const rows = bandRows(summaryOf(STATE), 'observe')
  expect(rows.row1).toBe('brainstorm  ●●●●✗◆○○  step 6 of 8: Step 6')
  expect(rows.mode).toBe('observe mode')
  expect(rows.button).toBe('Switch to enforce')
  expect(rows.flag).toBe('step 5 had no answer')
  expect(bandRows(summaryOf(STATE), 'enforce').button).toBe('Switch to observe')
  expect(fit('abcdef', 4)).toBe('abc…')
  expect(fit('abc', 4)).toBe('abc')
})

test('flags are toasted once, refused at the write gate, and given to the model at the report gate', () => {
  const first = newFlagToasts(STATE, [])
  expect(first.toasts).toEqual(['✗ Step 5 skipped: step 5 had no answer'])
  expect(newFlagToasts(STATE, first.keys).toasts).toEqual([])
  const refusal = refusalText(STATE, 4)
  expect(refusal === null ? '' : refusal).toContain('- step 5 had no answer')
  expect(refusalText(STATE, 5)).toBe(null)
  expect(refusalText({ ...STATE, gate: { ...STATE.gate, refuse: false } }, 4)).toBe(null)
  expect(reportContext(STATE)).toBe(null)
  const report = reportContext({ ...STATE, gate: { kind: 'report', seq: 4, refuse: true, reason: 'x' } })
  expect(report === null ? -1 : report.seq).toBe(4)
})

test('the final evaluation at session end uses what the budget leaves, or none', () => {
  expect(finalTimeout(1500)).toBe(1200)
  expect(finalTimeout(600)).toBe(300)
  expect(finalTimeout(450)).toBe(null)
  expect(finalTimeout(0)).toBe(null)
})

test('the retention period is 7 to 3650 days, and 30 otherwise (DM-06)', () => {
  expect([7, 30, 3650].map(retentionOf)).toEqual([7, 30, 3650])
  expect([2, 0, -7, 3651, 7.5, undefined, null, 'soon'].map(retentionOf)).toEqual([30, 30, 30, 30, 30, 30, 30, 30])
})

test('the refusal names what clears each flag, and the decision line only for decision flags', () => {
  const stepOnly = refusalText({ ...STATE, run: 'r1', gate: { kind: 'write', seq: 4, refuse: true, reason: 'x' },
    flags: [{ gate: 'write', seq: 4, step: 1, type: 'skipped', message: 'step 1 has no evidence or tick' }] }, 4) ?? ''
  expect(stepOnly).toContain('To clear step 1:')
  expect(stepOnly).toContain("A tick only in your thinking doesn't count.")
  expect(stepOnly.includes("are the user's")).toBe(false)
  expect(stepOnly).toContain('devforgeai/progress/runs/r1/')
  const owned = { ...STATE, steps: STATE.steps.map(s => (s.n === 5 ? { ...s, userOwned: true } : s)) }
  const decision = refusalText({ ...owned, gate: { kind: 'write', seq: 4, refuse: true, reason: 'x' },
    flags: [
      { gate: 'write', seq: 4, step: 5, type: 'skipped', message: 'step 5 had no answer from you' },
      { gate: 'write', seq: 4, step: 6, type: 'rule-broken', message: 'BRN-002.md sets disposition: promoted' },
    ] }, 4) ?? ''
  expect(decision).toContain("The decisions at step 5, 6 are the user's: ask the user, or leave those fields open.")
  expect(decision.includes('To clear step')).toBe(false)
})

// ---- the task list (SPEC-013 v4 and v5) ----

test('the task list: which sessions have one, which runs follow it, and how tasks map to steps', () => {
  expect(hasTaskList(['Read', 'TaskCreate', 'TaskUpdate'])).toBe(true)
  expect(hasTaskList(['TodoWrite'])).toBe(true)
  expect(hasTaskList(['TaskCreate'])).toBe(false)
  expect(hasTaskList(['TaskStop', 'ToolSearch'])).toBe(false)
  const loaded = (taskList: unknown, checklist: string) => JSON.stringify({ kind: 'skill-loaded', taskList, checklist })
  expect(followsTaskList(loaded(true, '- [ ] 1. A\nmetadata devforgeai_step: N'))).toBe(true)
  expect(followsTaskList(loaded(false, 'devforgeai_step'))).toBe(false)
  expect(followsTaskList(loaded(true, '- [ ] 1. A'))).toBe(false)
  expect(followsTaskList('not json')).toBe(false)
  expect(taskIdOf('Task #12 created successfully: 3. Evaluate')).toBe('12')
  expect(taskIdOf('Created.')).toBe(null)
  expect(stepOfTask({ subject: 'Anything', metadata: { devforgeai_step: 4 } })).toBe(4)
  expect(stepOfTask({ subject: '7. Resolve', metadata: { devforgeai_step: 2.5 } })).toBe(7)
  expect(stepOfTask({ subject: 'Tidy up' })).toBe(null)
  expect(stepOfTask({ subject: '0. Nothing' })).toBe(null)
  expect([stepStateOf('in_progress'), stepStateOf('completed'), stepStateOf('pending'), stepStateOf('deleted')])
    .toEqual(['started', 'done', null, null])
  const first = todoSteps([
    { content: '1. A', status: 'completed' }, { content: '2. B', status: 'in_progress' },
    { content: 'Notes', status: 'in_progress' }, { content: '3. C', status: 'pending' },
  ], {})
  expect(first.events).toEqual([{ step: 1, state: 'done' }, { step: 2, state: 'started' }])
  const second = todoSteps([{ content: '2. B', status: 'completed' }, { content: '3. C', status: 'in_progress' }], first.statuses)
  expect(second.events).toEqual([{ step: 2, state: 'done' }, { step: 3, state: 'started' }])
  expect(second.statuses).toEqual({ 1: 'completed', 2: 'completed', 3: 'in_progress' })
  // Version 6: the list the call replaced decides, so a todo already completed there claims nothing.
  const replaced = todoSteps([{ content: '2. B', status: 'completed' }, { content: '4. D', status: 'in_progress' }], {},
    [{ content: '2. B', status: 'completed' }, { content: '4. D', status: 'pending' }])
  expect(replaced.events).toEqual([{ step: 4, state: 'started' }])
  // Review: an earlier run's completed todos kept in the list, the new run's appended with the same numbers.
  const earlier = [{ content: '1. A', status: 'completed' }, { content: '2. B', status: 'completed' }]
  const appended = todoSteps([...earlier, { content: '1. A', status: 'in_progress' }, { content: '2. B', status: 'pending' }],
    {}, [...earlier, { content: '1. A', status: 'pending' }, { content: '2. B', status: 'pending' }])
  expect(appended.events).toEqual([{ step: 1, state: 'started' }])
})

test('the question gate refuses at its own seq; the adherence notice needs a report or an end, and the counts', () => {
  const question = { ...STATE, gate: { kind: 'question', seq: 9, refuse: true, reason: 'x' } }
  expect(questionRefusal(question, 9)).toBe(QUESTION_REFUSAL + QUESTION_TAG)
  expect(questionRefusal(question, 9, null, true)).toBe(QUESTION_REFUSAL)  // a tagged question isn't told to tag it
  expect(questionRefusal(question, 8)).toBe(null)
  expect(questionRefusal({ ...question, gate: { ...question.gate, refuse: false } }, 9)).toBe(null)
  expect(questionRefusal(STATE, 4)).toBe(null)
  const report = { ...STATE, gate: { kind: 'report', seq: 9, refuse: false, reason: null } }
  expect(adherenceText({ ...report, counts: { stepEvents: 5, unmarkedQuestions: 1 } }))
    .toBe("brainstorm didn't keep its task list: 5 step events, 1 questions asked without their step marked and tagged. "
      + 'Recommended: fix the skill so it keeps its checklist in the task list (DevForgeAI SPEC-012 §4)')
  expect(adherenceText({ ...report, counts: { stepEvents: 5, unmarkedQuestions: 0 } })).toBe(null)
  expect(adherenceText({ ...STATE, counts: { stepEvents: 0, unmarkedQuestions: 0 } })).toBe(null)
  expect(adherenceText({ ...STATE, ended: 'session-end', counts: { stepEvents: 0, unmarkedQuestions: 0 } })).not.toBe(null)
  expect(adherenceText({ ...STATE, ended: 'session-end' })).toBe(null)
  // The report reached, though a later write moved the gate on before the timer evaluated.
  const reported = { ...STATE, steps: STATE.steps.map(s => (s.n === 8 ? { ...s, kind: 'report', state: 'done' } : s)),
    counts: { stepEvents: 0, unmarkedQuestions: 0 } }
  expect(adherenceText(reported)).not.toBe(null)
})

// SPEC-013 v9 (BEH-25): the stuck notice's last sentence follows the refused flag's type and whether its step is
// user-owned, never its message text.
const TASK_ADVICE = "Help Claude bring its task list in step, or switch to observe mode with the band's button."
const EVIDENCE_ADVICE = "Claude hasn't done that step in a way the tracker can see: ask Claude to do it as the message "
  + "says, or switch to observe mode with the band's button."
const DECISION_ADVICE = "The refused write records a decision that needs your answer: answer Claude's question about it, "
  + "or ask Claude to leave it open, or switch to observe mode with the band's button."

test("the stuck notice's advice follows the refused flag's type and whether its step is user-owned (version 9)", () => {
  for (const type of ['unmarked-question', 'untagged-question', 'mismatched-question']) {
    expect(stuckAdvice(type, false)).toBe(TASK_ADVICE)
    expect(stuckAdvice(type, true)).toBe(TASK_ADVICE)
  }
  expect(stuckAdvice('skipped', false)).toBe(EVIDENCE_ADVICE)
  expect(stuckAdvice('claimed-not-evidenced', false)).toBe(EVIDENCE_ADVICE)
  expect(stuckAdvice('skipped', true)).toBe(DECISION_ADVICE)
  expect(stuckAdvice('rule-broken', false)).toBe(DECISION_ADVICE)
  expect(stuckAdvice('rule-broken', true)).toBe(DECISION_ADVICE)
  // The cause reads userOwned from the state's steps; a step the state doesn't list isn't user-owned.
  const owned = { ...STATE, gate: { kind: 'write', seq: 7, refuse: true, reason: 'x' },
    steps: STATE.steps.map(s => (s.n === 5 ? { ...s, userOwned: true } : s)),
    flags: [{ gate: 'write', seq: 7, step: 5, type: 'skipped', message: 'm5' },
      { gate: 'write', seq: 7, step: 6, type: 'rule-broken', message: 'm6' }] }
  expect(refusalCause(owned, 7)).toEqual({ key: 'write:skipped:5', step: 5, message: 'm5', type: 'skipped', userOwned: true })
  const unlisted = { ...owned, flags: [{ gate: 'write', seq: 7, step: 40, type: 'skipped', message: 'm40' }] }
  expect(refusalCause(unlisted, 7)?.userOwned).toBe(false)
  expect(stuckText('brainstorm', 5, 'm5', DECISION_ADVICE))
    .toBe(`brainstorm: the progress tracker refused Claude twice at step 5 for the same reason: m5. ${DECISION_ADVICE}`)
})

// VER-38 (SPEC-013 v12): after run-end stopped, the status line and the band say where the run stopped.
test('VER-38: a stopped run reads stopped at step n in the status line and the band', () => {
  const steps = Array.from({ length: 11 }, (_, i) => ({ n: i + 1, title: `Step ${i + 1}`, state: i < 8 ? 'done' : 'pending' }))
  const state = { skill: 'architecture', current: 8, ended: 'stopped', steps, flags: [],
    gate: { kind: null, seq: null, refuse: false, reason: null }, manifest: { state: 'matched' as const } }
  const summary = summaryOf(state)
  expect(statusText(summary, 'enforce', false, null)).toBe('architecture stopped at step 8 · enforce')
  expect(bandRows(summary, 'observe').row1.endsWith('  stopped at step 8')).toBe(true)
  const ended = summaryOf({ ...state, ended: 'clear' })
  expect(statusText(ended, 'observe', false, null)).toBe('architecture ended')
})

// VER-43 and VER-44 (SPEC-013 v14; version 13's VER-41 replaced): the trail's texts, which paused run a TaskUpdate
// names, the trail kept at a load, a log's run-end reason, the nested display and exit texts.
test('VER-43: the return line asks to re-mark the step; the trail note names the whole trail, top first', () => {
  expect(returnLine('architecture', 7)).toBe("This skill was loaded by architecture at step 7. When this skill's work is done, mark architecture's step 7 task in progress again and continue architecture at step 7.")
  const trail = [{ skill: 'brainstorm', step: 4, tasks: { '1': 1, '4': 4 } }, { skill: 'architecture', step: 7, tasks: { '7': 7, '8': 8 } }]
  expect(trailNote('spec-lookup', trail)).toBe('Return points (from the progress tracker): when spec-lookup is done, continue architecture at step 7; then brainstorm at step 4.')
})

test('VER-44: a TaskUpdate names the topmost paused run holding its task ID, whatever its status', () => {
  const trail = [{ skill: 'brainstorm', step: 4, tasks: { '1': 1, '4': 4, '7': 7 } }, { skill: 'architecture', step: 7, tasks: { '7': 7, '8': 8 } }]
  expect(pausedWith(trail, '7')).toBe(1)
  expect(pausedWith(trail, '8')).toBe(1)
  expect(pausedWith(trail, '1')).toBe(0)
  expect(pausedWith(trail, '99')).toBe(-1)
  expect(pausedWith(trail, 7)).toBe(-1)
  expect(pausedWith([], '7')).toBe(-1)
})

test('VER-43: a trail read back after a load keeps only entries with their run (version 13 entries are dropped)', () => {
  const run = { id: 'r1', skill: 'architecture', seq: 9, dir: '/d', root: '/w' }
  const full = { skill: 'architecture', step: 7, tasks: { '7': 7 }, run, summary: null, marked: false, shown: [], contextSent: [],
    todos: {}, adhered: null, refusals: {}, refused: [], reviewed: null }
  expect(keptTrail([{ skill: 'brainstorm', step: 4, tasks: {} }, full])).toEqual([full])
  expect(keptTrail(null)).toEqual([])
  expect(keptTrail([{ ...full, step: '7' }, { ...full, run: null }])).toEqual([])
})

test('VER-44: a log\'s run-end reason; a session end\'s room for one more run-end', () => {
  const line = (kind: string, fields = {}) => JSON.stringify({ kind, ...fields })
  expect(endReason([line('skill-loaded'), line('tool'), line('run-end', { reason: 'stopped' }), line('turn')])).toBe('stopped')
  expect(endReason([line('skill-loaded')])).toBe(null)
  expect(hasRoom(1500)).toBe(true)
  expect(hasRoom(299)).toBe(false)
})

test('VER-43: the status line and the band name the run just beneath, and count the rest', () => {
  const steps = Array.from({ length: 5 }, (_, i) => ({ n: i + 1, title: `Step ${i + 1}`, state: i < 1 ? 'done' : i === 1 ? 'current' : 'pending' }))
  const s = summaryOf({ skill: 'spec-lookup', current: 2, ended: null, steps, flags: [],
    gate: { kind: null, seq: null, refuse: false, reason: null }, manifest: { state: 'matched' as const } })
  const arch = { skill: 'architecture', step: 7, summary: { steps: 11 } }
  const brn = { skill: 'brainstorm', step: 4, summary: null }
  expect(statusText(s, 'observe', false, null, [arch])).toBe('spec-lookup 2/5 · in architecture 7/11')
  expect(statusText(s, 'enforce', false, null, [brn, arch])).toBe('spec-lookup 2/5 · in architecture 7/11 · 1 more · enforce')
  expect(statusText(s, 'observe', false, null, [brn])).toBe('spec-lookup 2/5 · in brainstorm 4')
  expect(statusText(s, 'observe', false, null, [])).toBe('spec-lookup 2/5')
  expect(bandRows(s, 'observe', [arch]).row1.endsWith(' (paused: architecture at step 7)')).toBe(true)
  expect(bandRows(s, 'observe', [brn, arch]).row1.endsWith(' (paused: architecture at step 7, 1 more)')).toBe(true)
  expect(bandRows(s, 'observe').row1.endsWith('step 2 of 5: Step 2')).toBe(true)
})

test('VER-44: the exit question and the kept text while a run is paused beneath the open one', () => {
  const b = { skill: 'architecture', step: 7 }
  const at = { current: 2, steps: 5, ended: null }
  expect(nestedExitQuestion('spec-lookup', at, b, 0, 'Clear')).toBe('spec-lookup run is at step 2 of 5 and unfinished (architecture paused at step 7). Clear anyway?')
  expect(nestedExitQuestion('spec-lookup', { ...at, current: null }, b, 1, 'Exit')).toBe('spec-lookup run is done (architecture paused at step 7, 1 more paused). Exit anyway?')
  expect(nestedExitQuestion('spec-lookup', null, b, 0, 'Resume')).toBe('spec-lookup run has just started (architecture paused at step 7). Resume anyway?')
  expect(nestedKeptText('spec-lookup', b)).toBe('Kept working: the spec-lookup run goes on, and architecture is still paused at step 7.')
})

// ---- the offer to continue an earlier run (SPEC-013 v16), after the build reviews: states as the evaluator writes them ----

/** A brainstorm state as evaluate.py writes it: each step's state and evidence types, its claim, and how the run ended. */
function brnState(steps: Array<[string, string[], string | null]>, ended: string | null = 'session-end'): ProgressState {
  const titles = ['Intake', 'Framework', 'Diverge', 'Evaluate', 'Dispositions', 'Write the BRN', 'Validate', 'Report']
  return { skill: 'brainstorm', current: null, ended, flags: [], gate: { kind: null, seq: null, refuse: false, reason: null },
    manifest: { state: 'matched' },
    steps: steps.map(([state, types, claim], i) => ({ n: i + 1, title: titles[i], state, userOwned: i + 1 === 5,
      evidence: types.map(type => ({ type })), claim: claim === null ? null : { state: claim } })) } as unknown as ProgressState
}
const line = (o: object) => JSON.stringify({ run: 'x', seq: 1, time: '2026-10-05T10:00:00Z', ...o })

test('VER-47 (review S1): a resumed run resumed again keeps its carried write step written and its answered decisions', () => {
  // Run B continued run A at step 7 (A had answered step 5 and written the BRN): B's steps 1 to 6 are carried, step 5
  // answered in A; B marked step 7 and ended.
  const b = brnState([['carried', ['carried'], 'done'], ['carried', ['carried'], 'done'], ['carried', ['carried'], 'done'],
    ['carried', ['carried'], 'done'], ['carried', ['carried'], 'done'], ['carried', ['carried'], 'done'],
    ['pending', [], null], ['pending', [], null]])
  const lines = [line({ kind: 'skill-loaded', resumes: 'A', carried: [1, 2, 3, 4, 5, 6], answered: [5] }),
    line({ seq: 2, kind: 'step', step: 7, state: 'started' }), line({ seq: 3, kind: 'run-end', reason: 'session-end' })]
  const plan = resumePlan('B', b, lines, 6, Date.parse('2026-10-05T12:00:00Z'))
  expect(plan).toMatchObject({ step: 7, carried: [1, 2, 3, 4, 5, 6], answered: [5], owned: [] })
})

test('VER-47 (review S2): a decision written before the user\'s answer is no answer that stands', () => {
  // The BRN was written with dispositions promoted before step 5 was answered (step 5 skipped, step 6 rule-broken); the
  // user answered step 5 afterwards; the run ended marked at step 7.
  const a = brnState([['done', ['read'], 'done'], ['done', [], 'done'], ['done', [], 'done'], ['done', [], 'done'],
    ['skipped', ['answer'], 'done'], ['rule-broken', ['write'], 'done'], ['pending', [], null], ['pending', [], null]])
  const lines = [line({ kind: 'skill-loaded' }), line({ seq: 2, kind: 'step', step: 7, state: 'started' }),
    line({ seq: 3, kind: 'run-end', reason: 'session-end' })]
  const plan = resumePlan('A', a, lines, 6, Date.parse('2026-10-05T12:00:00Z'))
  expect(plan).toMatchObject({ step: 7, answered: [], owned: [5] })
})

test('VER-47 (review S3): the offer looks for runs under the name run IDs give the skill', () => {
  expect(runName('My_Skill')).toBe('my-skill')
  expect(runName('9lives')).toBe('lives')
  expect(runName('brainstorm')).toBe('brainstorm')
  expect(runId(T0, 'My_Skill', new Uint8Array([0, 0, 0, 1]))).toBe(`20261002T120000Z-${runName('My_Skill')}-00000001`)
})

test('BEH-31 (review notes): an unknown age and a path with a line break don\'t break the question', () => {
  expect(ageText(NaN)).toBe('an unknown time')
  const a = brnState([['done', ['read'], 'done'], ['pending', [], null], ['pending', [], null], ['pending', [], null],
    ['pending', [], null], ['pending', [], null], ['pending', [], null], ['pending', [], null]])
  const lines = [line({ kind: 'skill-loaded' }), line({ seq: 2, kind: 'tool', tool: 'Write', path: 'docs/a\nb.md', error: false }),
    line({ seq: 3, kind: 'run-end', reason: 'session-end' })]
  const plan = resumePlan('A', a, lines, 6, Date.parse('2026-10-05T12:00:00Z'))!
  expect(resumeQuestion('brainstorm', plan)).toBe('brainstorm: an earlier run ended at step 2 of 8 on 2026-10-05 (session end). It wrote docs/a b.md. Continue it?')
})
