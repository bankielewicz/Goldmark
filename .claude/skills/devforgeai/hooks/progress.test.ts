import { expect, mock, test } from 'claude-code/testing'
import type { Plugin } from 'claude-code/testing'

// The adapter driven through the test kit (SPEC-013 §9). The kit stands for Claude Code: the world below answers
// every call the module makes on $, so nothing touches disk (P12); the module's file writes are captured, its
// process runs (python, settings.py, evaluate.py) answered from a table, and its clock is mocked.

const ROOT = '/work'
const T0 = Date.UTC(2026, 9, 2, 12, 0, 0)
const PROGRESS = `${ROOT}/devforgeai/progress`
const SESSION = `${PROGRESS}/sessions/s1`
const SKILLS = ['architecture', 'brainstorm', 'context', 'documents-updater', 'epic', 'git', 'prd', 'spec-lookup']
const CHECKLIST = 'Base directory for this skill: /x\n\n- [ ] 1. Intake\n- [ ] 2. Pick'

type Any = any // the kit's stubs take loosely typed events; the module itself is typed

type Over = {
  surfaces?: string[]
  python3?: boolean
  mode?: string
  modeStderr?: string
  setMode?: Any
  failSessionId?: boolean
  failVersion?: boolean
  failExists?: (path: string) => boolean
  failStatus?: boolean
  evaluate?: (argv: readonly string[]) => Any
  tool?: (e: Any) => Any
  failWrite?: (path: string) => boolean
  files?: Record<string, string>
  prune?: (argv: readonly string[]) => Any
  toolList?: string[] | 'reject'
  compact?: (e: Any) => Any
}

type World = {
  files: Map<string, string>
  writes: string[]
  toasts: string[]
  logs: string[]
  statuses: Array<string | undefined>
  runs: string[][]
  contexts: string[][]
  tools: string[]
  clock: Any
  root: string
  sessionId: string
  inits: Any[]
  pruneSawRun: boolean[]
  compactIn: Any[]
}

const STATE = {
  format: 'devforgeai-progress/1', run: 'r', skill: 'brainstorm', current: 2, ended: null,
  manifest: { state: 'matched', manifestHash: null, checklistHash: null, layers: [] },
  steps: [
    { n: 1, title: 'Intake', state: 'done' },
    { n: 2, title: 'Pick', state: 'current' },
  ],
  flags: [],
  gate: { kind: null, seq: null, refuse: false, reason: null },
  next: null,
  counts: {},
}

function listOf(files: Map<string, string>, path: string): Any[] {
  if (path.endsWith('/skills')) return SKILLS.map(name => ({ name, kind: 'dir', size: 0 }))
  const prefix = path.replace(/\/+$/, '') + '/'
  const names = new Map<string, string>()
  for (const key of files.keys()) {
    if (!key.startsWith(prefix)) continue
    const rest = key.slice(prefix.length)
    const name = rest.split('/')[0]
    names.set(name, rest.includes('/') ? 'dir' : 'file')
  }
  return [...names].map(([name, kind]) => ({ name, kind, size: 0 }))
}

function processRun(w: World, over: Over, argv: readonly string[]): Any {
  if (argv[1] === '--version') {
    if (argv[0] === 'python3' && over.python3 !== false) return { value: { exitCode: 0, stdout: 'Python 3.12.3\n', stderr: '' } }
    return { deny: `failed to start: ENOENT: Executable not found in $PATH: "${argv[0]}"` }
  }
  const script = argv[1] ?? ''
  if (script.endsWith('/progress/settings.py') && argv[2] === 'mode') {
    return { value: { exitCode: 0, stdout: `${over.mode ?? 'observe framework-default'}\n`, stderr: over.modeStderr ?? '' } }
  }
  if (script.endsWith('/progress/settings.py') && argv[2] === 'set-mode') {
    const value = argv[argv.indexOf('--value') + 1]
    return over.setMode ?? { value: { exitCode: 0, stdout: `saved progress.mode=${value} to .claude/devforgeai.local.md\n`, stderr: '' } }
  }
  if (script.endsWith('/progress/evaluate.py') && argv[2] === 'evaluate') {
    const out = argv[argv.indexOf('--out') + 1]
    const answer = over.evaluate?.(argv) ?? { state: STATE }
    if (answer.deny !== undefined) return { deny: answer.deny }
    if (answer.state !== undefined) w.files.set(out, JSON.stringify(answer.state))
    if (answer.raw !== undefined) w.files.set(out, answer.raw)
    return { value: { exitCode: answer.exitCode ?? 0, stdout: 'progress brainstorm: step 2 of 2, 0 flags\n', stderr: answer.stderr ?? '' } }
  }
  if (script.endsWith('/progress/prune.py') && argv[2] === 'prune') {
    const runs = `${argv[argv.indexOf('--root') + 1]}/devforgeai/progress/runs/`
    w.pruneSawRun.push([...w.files.keys()].some(k => k.startsWith(runs) && k.endsWith('/events.jsonl')))
    return over.prune?.(argv) ?? { value: { exitCode: 0, stdout: 'pruned 0 runs, 0 sessions\n', stderr: '' } }
  }
  return { deny: `unexpected process: ${argv.join(' ')}` }
}

// Every stub the module can reach, registered once (the kit allows one on() per event in a test).
function world(on: Any, over: Over = {}): World {
  const w: World = {
    files: new Map(Object.entries(over.files ?? {})), writes: [], toasts: [], logs: [], statuses: [], runs: [],
    contexts: [], tools: [],
    clock: mock.clock(on, { now: T0 }),
    root: ROOT, sessionId: 's1', inits: [], pruneSawRun: [], compactIn: [],
  }
  on('session.root', () => ({ value: w.root }))
  on('session.id', () => (over.failSessionId ? { deny: 'no session id' } : { value: w.sessionId }))
  on('session.version', () => (over.failVersion ? { deny: 'no version' } : { value: { version: '2.1.287' } }))
  on('session.surfaces', () => ({ value: over.surfaces ?? ['terminal'] }))
  // The session's tools: by default the task tools are among them, as with the opt-in (SPEC-013 §9).
  on('tool.list', () => (over.toolList === 'reject'
    ? { deny: 'tool list unavailable\nsecond line of the reason' }
    : { value: (over.toolList ?? ['Read', 'Write', 'TaskCreate', 'TaskUpdate', 'ToolSearch']).map(name => ({ name, description: '', mcp: false })) }))
  on('session.start', (_$: Any, e: Any) => ({ cwd: e.cwd }))
  on('classic.SessionStart', () => ({}))
  on('skill.prompt', (_$: Any, e: Any) => ({ text: e.text }))
  on('prompt.compose', () => ({ sections: [] }))
  on('prompt.submit', (_$: Any, e: Any) => {
    w.contexts.push([...(e.context ?? [])])
    return { text: e.text }
  })
  on('turn.start', (_$: Any, e: Any) => ({ turnId: e.turnId }))
  on('turn.complete', () => ({ text: '' }))
  on('session.end', (_$: Any, e: Any) => ({ sessionId: e.sessionId }))
  // The engine's compaction, beneath the adapter: it records what it was handed and returns a summary (SPEC-013 v7).
  on('session.compact', (_$: Any, e: Any) => {
    w.compactIn.push(e)
    return over.compact?.(e) ?? { messages: [{ role: 'user', text: 'the summary', toolUses: [] }] }
  })
  on('ui.render', () => ({ type: 'Text', props: {}, children: ['drawn by the mods after it'] }))
  on('ui.toast', (_$: Any, e: Any) => { w.toasts.push(e.text); return { value: undefined } })
  on('ui.log', (_$: Any, e: Any) => { w.logs.push(e.text); return { value: undefined } })
  on('ui.status', (_$: Any, e: Any) => {
    if (over.failStatus) return { deny: 'status line unavailable' }
    w.statuses.push(e.text)
    return { value: undefined }
  })
  on('fs.exists', (_$: Any, e: Any) => over.failExists?.(e.path) ? { deny: `EIO: ${e.path}` } : ({
    value: w.files.has(e.path) || [...w.files.keys()].some(k => k.startsWith(e.path.replace(/\/+$/, '') + '/')),
  }))
  on('fs.read', (_$: Any, e: Any) => (w.files.has(e.path) ? { value: w.files.get(e.path) } : { deny: `ENOENT: ${e.path}` }))
  on('fs.write', (_$: Any, e: Any) => {
    if (over.failWrite?.(e.path)) return { deny: `EACCES: ${e.path}` }
    w.files.set(e.path, e.text)
    w.writes.push(e.path)
    return { value: undefined }
  })
  on('fs.list', (_$: Any, e: Any) => ({ value: listOf(w.files, e.path) }))
  on('process.run', (_$: Any, e: Any) => {
    w.runs.push([...e.argv])
    w.inits.push(e.init ?? {})
    return processRun(w, over, e.argv)
  })
  on('tool.call', (_$: Any, e: Any) => {
    w.tools.push(e.tool)
    return over.tool?.(e) ?? { result: 'ok' }
  })
  return w
}

async function start($: Any, interactive = true) {
  await $.session.start({ surface: interactive ? 'terminal' : null, isInteractive: interactive, cwd: ROOT })
}

async function load($: Any, skill = 'devforgeai:brainstorm', text = CHECKLIST) {
  await $.skill.prompt({ skill, text })
}

/** session.append can't complete in the kit (nothing answers beneath it), so its rejection is expected. */
async function respond($: Any, content: Any, agentId?: string) {
  try {
    await $.session.append({ door: 'response', uuid: `u${Math.random()}`, origin: { kind: 'model' }, agentId,
      message: { type: 'assistant', role: 'assistant', content } })
  } catch {
    // no implementation for session.append beneath the test
  }
}

function runFiles(w: World, name: string): string[] {
  return [...w.files.keys()].filter(k => k.startsWith(`${PROGRESS}/runs/`) && k.endsWith(`/${name}`)).sort()
}

/** The event logs of a skill's runs, in the order the runs were created, with the random digits normalised. */
function runsOf(w: World, skill = 'brainstorm'): string[][] {
  const order: string[] = []
  for (const path of w.writes) {
    if (path.startsWith(`${PROGRESS}/runs/`) && path.endsWith('/events.jsonl') && path.includes(`-${skill}-`) && !order.includes(path)) order.push(path)
  }
  return order.map(f => (w.files.get(f) ?? '').split('\n').filter(Boolean).map(l => l.replace(/-[0-9a-f]{8}"/, '-RANDOM8"')))
}

/** The events of the latest run of a skill. */
function eventsOf(w: World, skill = 'brainstorm'): string[] {
  const runs = runsOf(w, skill)
  return runs.length ? runs[runs.length - 1] : []
}

function kinds(lines: string[]): string[] {
  return lines.map(l => JSON.parse(l).kind)
}

const asker: Plugin = {
  name: 'asker',
  register(on) {
    on('command.run', { command: 'ask-me' }, async $ => {
      try {
        await $.ui.ask('Which one?', ['A', 'B'])
      } catch {
        // dismissed, or nobody to ask
      }
      return { text: 'asked' }
    })
  },
}

function bottomTool(e: Any): Any {
  if (e.tool === 'AskUserQuestion') {
    const q = e.questions?.[0]?.question ?? 'Q?'
    if (q === 'Dismiss me?') return { result: "Error: The user doesn't want to proceed with this tool use.", isError: true, text: "The user doesn't want to proceed with this tool use." }
    if (q === 'Type me?') return { result: { questions: e.questions, answers: { [q]: 'not sure' }, annotations: {} } }
    return { result: { questions: e.questions ?? [], answers: { [q]: 'Yes' }, annotations: {} } }
  }
  if (e.tool === 'Bash' && e.command === 'exit 3') return { result: 'Error: Exit code 3', isError: true, text: 'Exit code 3' }
  if (e.tool === 'Write' && e.file_path === `${ROOT}/tmp/refused.md`) return { deny: 'refused by a settings hook' }
  return { result: 'ok' }
}

// EXPECTED-EVENTS-BEGIN (VER-04; test_adapter_structure.py reads these lines and validates each against events.schema.json)
const EXPECTED_EVENTS = [
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":1,"time":"2026-10-02T12:00:00Z","kind":"skill-loaded","format":"devforgeai-events/1","skill":"brainstorm","checklist":"Base directory for this skill: /x\\n\\n- [ ] 1. Intake\\n- [ ] 2. Pick","host":"claude-code 2.1.287","taskList":true,"mode":"observe","modeSource":"framework-default"}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":2,"time":"2026-10-02T12:00:00Z","kind":"tool","tool":"Read","path":"docs/specs/brainstorm/BRN-001.md","exit":0,"error":false}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":3,"time":"2026-10-02T12:00:00Z","kind":"tool","tool":"Bash","command":"python3 scripts/validate_brn.py docs/specs/brainstorm/BRN-001.md","exit":0,"error":false}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":4,"time":"2026-10-02T12:00:00Z","kind":"answer","answered":true}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":5,"time":"2026-10-02T12:00:00Z","kind":"answer","answered":true}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":6,"time":"2026-10-02T12:00:00Z","kind":"answer","answered":false}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":7,"time":"2026-10-02T12:00:00Z","kind":"tool","tool":"Bash","command":"exit 3","exit":3,"error":true}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":8,"time":"2026-10-02T12:00:00Z","kind":"tool","tool":"Write","path":"tmp/refused.md","exit":null,"error":true,"content":"no"}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":9,"time":"2026-10-02T12:00:00Z","kind":"turn","phase":"start"}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":10,"time":"2026-10-02T12:00:00Z","kind":"reply","text":"- [x] 1. Intake"}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":11,"time":"2026-10-02T12:00:00Z","kind":"reply","text":"- [x] 2. Pick"}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":12,"time":"2026-10-02T12:00:00Z","kind":"turn","phase":"end"}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":13,"time":"2026-10-02T12:00:00Z","kind":"tool","tool":"Write","path":"docs/specs/brainstorm/BRN-001.md","exit":0,"error":false,"content":"---\\nid: BRN-001\\n---\\n"}',
  '{"run":"20261002T120000Z-brainstorm-RANDOM8","seq":14,"time":"2026-10-02T12:00:00Z","kind":"prompt"}',
]
// EXPECTED-EVENTS-END

test('VER-04: a scripted session writes exactly the expected event lines', { plugins: [asker] }, async ($, on) => {
  const w = world(on, { tool: bottomTool })
  await start($)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/docs/specs/brainstorm/BRN-001.md` } as Any)
  await $.tool.call({ tool: 'Bash', command: 'python3 scripts/validate_brn.py docs/specs/brainstorm/BRN-001.md' } as Any)
  await $.tool.call({ tool: 'AskUserQuestion', questions: [{ question: 'Pick one?', header: 'Pick', options: [], multiSelect: false }] } as Any)
  await $.tool.call({ tool: 'AskUserQuestion', questions: [{ question: 'Type me?', header: 'Type', options: [], multiSelect: false }] } as Any)
  await $.tool.call({ tool: 'AskUserQuestion', questions: [{ question: 'Dismiss me?', header: 'No', options: [], multiSelect: false }] } as Any)
  await ($ as Any).command.run({ command: 'ask-me', args: '' })
  await $.tool.call({ tool: 'Bash', command: 'exit 3' } as Any)
  await $.tool.call({ tool: 'Write', file_path: `${ROOT}/tmp/refused.md`, content: 'no' } as Any)
  await ($ as Any).turn.start({ turnId: 't1' })
  await respond($, [{ type: 'text', text: '- [x] 1. Intake' }])
  await respond($, [{ type: 'tool_use', id: 'x', name: 'Bash', input: {} }])
  await respond($, [{ type: 'thinking', thinking: 'hmm' }])
  await respond($, [{ type: 'text', text: '- [x] 2. Pick' }])
  await ($ as Any).turn.complete({ turnId: 't1', answer: '- [x] 2. Pick', durationMs: 5, isAborted: false, reason: 'answer', usage: null })
  await $.tool.call({ tool: 'Write', file_path: `${ROOT}/docs/specs/brainstorm/BRN-001.md`, content: '---\nid: BRN-001\n---\n' } as Any)
  await ($ as Any).prompt.submit({ text: 'looks good', wait: false, origin: { kind: 'composer' } })
  await ($ as Any).prompt.submit({ text: '<task-notification>done</task-notification>', wait: false, origin: { kind: 'task-notification' } })
  await $.tool.call({ tool: 'Write', file_path: `${ROOT}/docs/x.md`, content: 'sub', agentId: 'a1' } as Any)
  await respond($, [{ type: 'text', text: '- [x] 9. subagent' }], 'a1')
  expect(eventsOf(w)).toEqual(EXPECTED_EVENTS)
}, )

test('VER-05: runs end at another skill or the same one, at /clear and at the session end; untracked skills do nothing', async ($, on) => {
  const w = world(on, { files: { [`${ROOT}/devforgeai/manifests/release.json`]: '{}' } })
  await start($)
  await load($)
  await load($, 'plugin-dev:create-plugin')
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded'])
  await load($, 'devforgeai:prd')
  const first = eventsOf(w)
  expect(JSON.parse(first[first.length - 1])).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  await load($, 'devforgeai:prd')
  const prd = runsOf(w, 'prd')
  expect(prd.length).toBe(2)
  expect(JSON.parse(prd[0].slice(-1)[0])).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  await load($, 'release')
  expect(kinds(eventsOf(w, 'release'))).toEqual(['skill-loaded'])
  await ($ as Any).session.end({ reason: 'clear', sessionId: 's1', resume: { id: 's1' } })
  expect(JSON.parse(eventsOf(w, 'release').slice(-1)[0])).toMatchObject({ kind: 'run-end', reason: 'clear' })
  for (const id of runFiles(w, 'events.jsonl')) {
    expect(/runs\/[0-9]{8}T[0-9]{6}Z-[a-z][a-z0-9-]*-[0-9a-f]{8}\/events\.jsonl$/.test(id)).toBe(true)
  }
})

test('VER-05: session end with any other reason is session-end, and nothing ends a run while idle', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  // The kit's mock clock runs at most 10,000 waits in one advance, so 31 minutes (past the 30-minute idle
  // display) stands for the spec's 24 hours: nothing in the adapter ends a run on time either way.
  for (let i = 0; i < 31; i++) await w.clock.advance(60 * 1000)
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded'])
  expect((w.statuses.filter(Boolean).slice(-1)[0] ?? '').endsWith(' · idle')).toBe(true)
  await ($ as Any).session.end({ reason: 'resume', sessionId: 's1', resume: { id: 's1' } })
  expect(JSON.parse(eventsOf(w).slice(-1)[0])).toMatchObject({ kind: 'run-end', reason: 'session-end' })
})

test('VER-05: after /clear, /resume or /branch, classic.SessionStart resolves the mode and the next run opens', async ($, on) => {
  const w = world(on, { mode: 'enforce local' })
  await start($)
  await ($ as Any).session.end({ reason: 'clear', sessionId: 's1', resume: { id: 's1' } })
  await ($ as Any).classic.SessionStart({ source: 'fork' })
  const modeRuns = w.runs.filter(a => a[2] === 'mode').length
  expect(modeRuns).toBe(2)
  await load($)
  expect(JSON.parse(eventsOf(w)[0])).toMatchObject({ kind: 'skill-loaded', mode: 'enforce', modeSource: 'local' })
})

test('VER-10: a headless session records, writes and draws nothing', async ($, on) => {
  const w = world(on)
  await start($, false)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  expect(w.writes).toEqual([])
  expect(w.statuses.filter(Boolean)).toEqual([])
})

test('VER-10: the print trait also marks a session headless', async ($, on) => {
  const w = world(on)
  await start($)
  const before = w.writes.length
  await ($ as Any).prompt.compose({ model: 'm', promptModel: 'm', surfaces: [], tools: [], traits: ['lean', 'print', 'skills'], outputStyle: null })
  await load($)
  expect(w.writes.slice(before)).toEqual([])
  expect(runFiles(w, 'events.jsonl')).toEqual([])
})

test('VER-10 / ERR-08: a tracked skill before any session.start records nothing', async ($, on) => {
  const w = world(on)
  await load($)
  expect(w.writes).toEqual([])
})

test('VER-10: with tracking off, nothing is written or drawn', { options: { tracking: 'off' } } as Any, async ($: Any, on: Any) => {
  const w = world(on)
  await start($)
  await load($)
  expect(w.writes).toEqual([])
  expect(w.runs).toEqual([])
})

test('VER-10: where nothing draws, the adapter records as usual and notices also go to the transcript', async ($, on) => {
  const w = world(on, { surfaces: [], python3: false })
  await start($)
  await load($)
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded'])
  expect(w.logs.some(l => l.includes('python not found'))).toBe(true)
})

test('VER-11: the files written are the progress folder\'s .gitignore, the run\'s log, and the session\'s current.json and adapter.log', async ($, on) => {
  const w = world(on, { files: { [`${ROOT}/.gitignore`]: 'node_modules\n' } })
  await start($)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  await w.clock.advance(600)
  const written = new Set(w.writes.map(p => p.replace(/runs\/[^/]+\//, 'runs/RUN/')))
  expect([...written].sort()).toEqual([
    `${PROGRESS}/.gitignore`, `${PROGRESS}/runs/RUN/events.jsonl`, `${SESSION}/adapter.log`, `${SESSION}/current.json`,
  ])
  expect(w.files.get(`${PROGRESS}/.gitignore`)).toBe('*\n')
  expect(w.files.get(`${ROOT}/.gitignore`)).toBe('node_modules\n')
  expect(w.writes.includes(`${ROOT}/.gitignore`)).toBe(false)
})

test('VER-11: a Write over 64 KiB is recorded without content', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  await $.tool.call({ tool: 'Write', file_path: `${ROOT}/big.md`, content: 'x'.repeat(64 * 1024 + 1) } as Any)
  const last = JSON.parse(eventsOf(w).slice(-1)[0])
  expect(last.kind).toBe('tool')
  expect(last.content).toBeUndefined()
})

test('VER-11 / ERR-11: past 3 MiB no content is kept, and at 4 MiB the run stops with a notice', { timeoutMs: 120000 }, async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  const chunk = 'y'.repeat(60 * 1024)
  for (let i = 0; i < 60; i++) await $.tool.call({ tool: 'Write', file_path: `${ROOT}/c${i}.md`, content: chunk } as Any)
  const lines = eventsOf(w)
  const withContent = lines.filter(l => JSON.parse(l).content !== undefined).length
  expect(withContent > 40 && withContent < 60).toBe(true)
  for (let i = 0; i < 200; i++) await $.tool.call({ tool: 'Bash', command: 'z'.repeat(30 * 1024) } as Any)
  const size = (w.files.get(runFiles(w, 'events.jsonl')[0]) ?? '').length
  expect(size <= 4 * 1024 * 1024).toBe(true)
  expect(w.statuses.includes('progress: off (event log full)')).toBe(true)
})

test('VER-11 / ERR-03: when devforgeai/progress/ can\'t be written, tracking stops for the session', async ($, on) => {
  const w = world(on, { failWrite: p => p.startsWith(PROGRESS) })
  await start($)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  expect(w.statuses.includes('progress: off (cannot write devforgeai/progress)')).toBe(true)
  expect(w.toasts.filter(t => t.includes('cannot write devforgeai/progress')).length).toBe(1)
})

test('VER-13: a second session.start, as after a reload, keeps the open run and its seq', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  await start($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/b.md` } as Any)
  const lines = eventsOf(w)
  expect(kinds(lines)).toEqual(['skill-loaded', 'tool', 'tool'])
  expect(lines.map(l => JSON.parse(l).seq)).toEqual([1, 2, 3])
  expect(runFiles(w, 'events.jsonl').length).toBe(1)
})

test('VER-14: an Edit records the file it will leave, or no content when it can\'t be computed', async ($, on) => {
  const file = `${ROOT}/docs/a.md`
  const w = world(on, { files: { [file]: 'one two one', [`${ROOT}/big.md`]: 'q'.repeat(64 * 1024 + 1) } })
  await start($)
  await load($)
  await $.tool.call({ tool: 'Edit', file_path: file, old_string: 'two', new_string: 'TWO', replace_all: false } as Any)
  await $.tool.call({ tool: 'Edit', file_path: file, old_string: 'one', new_string: '1', replace_all: true } as Any)
  await $.tool.call({ tool: 'Edit', file_path: file, old_string: 'one', new_string: '1', replace_all: false } as Any)
  await $.tool.call({ tool: 'Edit', file_path: file, old_string: 'missing', new_string: 'x', replace_all: false } as Any)
  await $.tool.call({ tool: 'Edit', file_path: `${ROOT}/big.md`, old_string: 'q', new_string: 'r', replace_all: true } as Any)
  const contents = eventsOf(w).slice(1).map(l => JSON.parse(l).content)
  expect(contents).toEqual(['one TWO one', '1 two 1', undefined, undefined, undefined])
})

// ---- Phase 5: evaluation and display ----

function manifestsOf(argv: string[]): string[] {
  return argv.flatMap((a, i) => (a === '--manifests' ? [argv[i + 1]] : []))
}

test('VER-06: the evaluator runs with IF-03 argv and the layer folders that exist, once for a burst', async ($, on) => {
  const w = world(on, { files: {
    [`${ROOT}/devforgeai/manifests/organization/x.json`]: '{}', [`${ROOT}/devforgeai/manifests/release.json`]: '{}',
  } })
  await start($)
  await load($)
  for (let i = 0; i < 5; i++) await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a${i}.md` } as Any)
  expect(w.runs.filter(a => a[2] === 'evaluate').length).toBe(0)
  await w.clock.advance(600)
  const evals = w.runs.filter(a => a[2] === 'evaluate')
  expect(evals.length).toBe(1)
  const argv = evals[0]
  expect(argv[0]).toBe('python3')
  expect(argv[1].endsWith('/progress/evaluate.py')).toBe(true)
  const layers = manifestsOf(argv)
  expect(layers[0].endsWith('/progress/manifests')).toBe(true)
  expect(layers.slice(1)).toEqual([`${ROOT}/devforgeai/manifests/organization`, `${ROOT}/devforgeai/manifests`])
  expect(argv.slice(argv.indexOf('--root'))).toEqual(['--root', ROOT])
  expect(argv[argv.indexOf('--events') + 1].endsWith('/events.jsonl')).toBe(true)
  expect(w.files.get(`${SESSION}/current.json`)).toBe(JSON.stringify(STATE))
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/b.md` } as Any)
  await w.clock.advance(600)
  expect(w.runs.filter(a => a[2] === 'evaluate').length).toBe(2)
})

test('VER-06: without project layers only the plugin\'s manifests are passed', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  await w.clock.advance(600)
  expect(manifestsOf(w.runs.filter(a => a[2] === 'evaluate')[0]).length).toBe(1)
})

const BAND = {
  plugin: 'devforgeai', component: 'AbovePrompt', requestId: 'band', viewport: { columns: 120, rows: 40 },
  props: { hasSurvey: false, isWorking: false, maxRows: 2, bodyColumns: 80, scroll: { offset: 0, bodyRows: 2 }, view: {} },
}

for (const surface of ['terminal', 'desktop'] as const) {
  test(`VER-12: the band on ${surface} shows the run and the mode button, and keeps what later mods draw`, async ($, on) => {
    const w = world(on)
    await start($)
    await load($)
    await w.clock.advance(600)
    const ui = await ($ as Any).ui.mount({ ...BAND, surface })
    expect(await ui.find({ text: /step 2 of 2: Pick/ })).toBeDefined()
    expect(await ui.find({ text: /drawn by the mods after it/ })).toBeDefined()
    const button = await ui.find({ key: 'progress-mode' })
    expect(button).toBeDefined()
    expect(button.props.hotkey).toBeUndefined()
    await ui.press({ key: 'progress-mode' })
    expect(w.runs.some(a => a[2] === 'set-mode' && a[a.length - 1] === 'enforce')).toBe(true)
    expect(w.statuses[w.statuses.length - 1]).toBe('brainstorm 2/2 · enforce')
    expect(w.toasts.some(t => t.includes('enforce mode, saved to .claude/devforgeai.local.md'))).toBe(true)
    await ui.unmount()
  })
}

test('VER-12: the band draws only row 1 in one row, nothing of its own during a survey or with no run, and fits the width', async ($, on) => {
  const w = world(on)
  await start($)
  const none = await ($ as Any).ui.mount({ ...BAND, surface: 'terminal' })
  expect(await none.find({ text: /brainstorm/ })).toBeUndefined()
  await none.unmount()
  await load($)
  await w.clock.advance(600)
  const one = await ($ as Any).ui.mount({ ...BAND, surface: 'terminal', props: { ...BAND.props, maxRows: 1, bodyColumns: 20 } })
  const row = await one.find({ type: 'Text', text: /brainstorm/ })
  expect(row).toBeDefined()
  expect(Array.from(String(row.children.join(''))).length <= 20).toBe(true)
  expect(await one.find({ key: 'progress-mode' })).toBeUndefined()
  await one.unmount()
  const survey = await ($ as Any).ui.mount({ ...BAND, surface: 'terminal', props: { ...BAND.props, hasSurvey: true } })
  expect(await survey.find({ text: /step 2 of 2/ })).toBeUndefined()
  expect(await survey.find({ text: /drawn by the mods after it/ })).toBeDefined()
  await survey.unmount()
})

test('VER-12: the status text is sent once while it stays the same, and a failed save keeps the mode', async ($, on) => {
  const w = world(on, { setMode: { value: { exitCode: 1, stdout: '', stderr: "settings: .claude/devforgeai.local.md isn't frontmatter-only; not changed\n" } } })
  await start($)
  await load($)
  for (let i = 0; i < 4; i++) await w.clock.advance(600)
  expect(w.statuses.filter(s => s === 'brainstorm 2/2').length).toBe(1)
  const ui = await ($ as Any).ui.mount({ ...BAND, surface: 'terminal' })
  await ui.press({ key: 'progress-mode' })
  expect(w.toasts.some(t => t.includes("couldn't save progress.mode") && t.includes('frontmatter-only'))).toBe(true)
  expect(w.statuses.some(s => (s ?? '').includes('enforce'))).toBe(false)
  await ui.unmount()
})

test('VER-12: each flag is toasted once, and ignored local entries give one toast at session start', async ($, on) => {
  const flagged = { ...STATE, flags: [{ gate: 'write', seq: 2, step: 1, type: 'skipped', message: 'step 1 had no answer' }] }
  const w = world(on, { evaluate: () => ({ state: flagged }), modeStderr: 'ignored .claude/devforgeai.local.md progress.mode (\'strict\' is not observe or enforce)\n' })
  await start($)
  expect(w.toasts.filter(t => t.startsWith('ignored .claude/devforgeai.local.md progress.mode')).length).toBe(1)
  await load($)
  await w.clock.advance(600)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  await w.clock.advance(600)
  expect(w.toasts.filter(t => t.startsWith('✗ Step 1 skipped')).length).toBe(1)
  expect(w.statuses[w.statuses.length - 1]).toBe('brainstorm 2/2 · 1 flag')
})

// ---- Phase 6: enforce mode and failing open ----

const WRITE = { tool: 'Write', file_path: `${ROOT}/docs/specs/brainstorm/BRN-001.md`, content: 'disposition: promoted' }

function refusingAt(seq: number) {
  return (argv: readonly string[]) => (argv.some(a => a.endsWith('/pending.jsonl'))
    ? { state: { ...STATE, gate: { kind: 'write', seq, refuse: true, reason: 'x' },
      flags: [{ gate: 'write', seq, step: 1, type: 'skipped', message: 'step 1 had no answer from you' }] } }
    : { state: STATE })
}

test('VER-07: in enforce mode the write gate\'s Write is refused before it runs, and recorded as an error', async ($, on) => {
  const w = world(on, { mode: 'enforce local', evaluate: refusingAt(2) })
  await start($)
  await load($)
  const r = (await $.tool.call(WRITE as Any)) as Any
  expect(typeof r.deny).toBe('string')
  expect(r.deny).toContain('step 1 had no answer from you')
  expect(w.tools.includes('Write')).toBe(false)
  expect(JSON.parse(eventsOf(w).slice(-1)[0])).toMatchObject({ kind: 'tool', tool: 'Write', exit: null, error: true })
  expect(w.writes.filter(p => p.endsWith('/pending.jsonl')).length).toBe(1)
  expect(runFiles(w, 'pending.json').length).toBe(1)
})

test('VER-07: with refuse false or a gate at another seq the Write goes on; observe mode checks nothing', async ($, on) => {
  const w = world(on, { mode: 'enforce local', evaluate: refusingAt(7) })
  await start($)
  await load($)
  const r = (await $.tool.call(WRITE as Any)) as Any
  expect(r.deny).toBeUndefined()
  expect(w.tools.includes('Write')).toBe(true)
  expect(JSON.parse(eventsOf(w).slice(-1)[0])).toMatchObject({ kind: 'tool', tool: 'Write', exit: 0, error: false })
})

test('VER-07: observe mode refuses nothing and writes no pending check', async ($, on) => {
  const w = world(on, { evaluate: refusingAt(2) })
  await start($)
  await load($)
  const r = (await $.tool.call(WRITE as Any)) as Any
  expect(r.deny).toBeUndefined()
  expect(w.writes.some(p => p.endsWith('/pending.jsonl'))).toBe(false)
})

test('VER-07: where nothing draws, the refusal also goes to the transcript', async ($, on) => {
  const w = world(on, { mode: 'enforce local', evaluate: refusingAt(2), surfaces: [] })
  await start($)
  await load($)
  await $.tool.call(WRITE as Any)
  expect(w.logs.some(l => l.includes('refused this write at the write gate'))).toBe(true)
})

function reporting(kind: string) {
  return () => ({ state: { ...STATE, current: null, gate: { kind, seq: 3, refuse: true, reason: 'x' },
    flags: [{ gate: kind, seq: 3, step: 1, type: 'skipped', message: 'step 1 was skipped' }] } })
}

test('VER-08: in enforce mode the report gate\'s flags reach the model once, with a prompt that isn\'t a command', async ($, on) => {
  const w = world(on, { mode: 'enforce local', evaluate: reporting('report') })
  await start($)
  await load($)
  await w.clock.advance(600)
  await ($ as Any).prompt.submit({ text: '/devforgeai:prd', wait: false, origin: { kind: 'composer' } })
  await ($ as Any).prompt.submit({ text: 'please fix it', wait: false, origin: { kind: 'composer' } })
  await ($ as Any).prompt.submit({ text: 'and again', wait: false, origin: { kind: 'composer' } })
  expect(w.contexts.map(c => c.length)).toEqual([0, 1, 0])
  expect(w.contexts[1][0]).toContain('step 1 was skipped')
})

test('VER-08: observe mode and a run-end gate add no context', async ($, on) => {
  const w = world(on, { evaluate: reporting('report') })
  await start($)
  await load($)
  await w.clock.advance(600)
  await ($ as Any).prompt.submit({ text: 'please fix it', wait: false, origin: { kind: 'composer' } })
  expect(w.contexts.map(c => c.length)).toEqual([0])
})

test('VER-08: a run-end gate adds no context in enforce mode', async ($, on) => {
  const w = world(on, { mode: 'enforce local', evaluate: reporting('end') })
  await start($)
  await load($)
  await w.clock.advance(600)
  await ($ as Any).prompt.submit({ text: 'please fix it', wait: false, origin: { kind: 'composer' } })
  expect(w.contexts.map(c => c.length)).toEqual([0])
})

const FAILURES: Array<[string, Over, string]> = [
  ['python missing', { python3: false }, 'progress: off (python not found)'],
  ['evaluator exit 2', { evaluate: () => ({ exitCode: 2, stderr: 'evaluate: bad manifest\n' }) }, 'progress: off (evaluator: evaluate: bad manifest)'],
  ['evaluator timeout', { evaluate: () => ({ deny: 'timed out after 5000 ms' }) }, 'progress: off (evaluator timed out)'],
  ['--out not JSON', { evaluate: () => ({ exitCode: 0, raw: 'not json' }) }, 'progress: off (evaluator: its state is not readable)'],
]

for (const [name, over, status] of FAILURES) {
  test(`VER-09: ${name}: every call goes on, events are recorded, and the user is told once`, async ($, on) => {
    const w = world(on, { ...over, mode: 'enforce local' })
    await start($)
    await load($)
    const r1 = (await $.tool.call(WRITE as Any)) as Any
    await w.clock.advance(600)
    const r2 = (await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)) as Any
    await w.clock.advance(600)
    expect(r1).toEqual({ result: 'ok' })
    expect(r2).toEqual({ result: 'ok' })
    expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'tool', 'tool'])
    expect(w.statuses.includes(status)).toBe(true)
    const reason = status.slice('progress: off ('.length, -1)
    expect(w.toasts.filter(t => t.includes(reason)).length).toBe(1)
    expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes(`fail-open: ${reason}`)).toBe(true)
  })
}

test('VER-09 / ERR-10: a timer callback that throws is caught, logged and treated as the evaluator failing', async ($, on) => {
  // The evaluation's own fs.exists fails, so the timer's code throws (a failed $.ui.status is only dropped).
  const w = world(on, { failExists: p => p.endsWith('/devforgeai/manifests/organization') })
  await start($)
  await load($)
  await w.clock.advance(600)
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes('fail-open: timer:')).toBe(true)
  expect(w.statuses.some(s => (s ?? '').startsWith('progress: off (timer:'))).toBe(true)
})

test('VER-09: a hook that throws before next passes its event on unchanged', async ($, on) => {
  const w = world(on, { failSessionId: true })
  const r = (await ($ as Any).session.start({ surface: 'terminal', isInteractive: true, cwd: ROOT })) as Any
  expect(r).toEqual({ cwd: ROOT })
  expect(w.toasts.some(t => t.startsWith('DevForgeAI progress: session.start:'))).toBe(true)
})

test('VER-09: a hook that fails after next keeps the result, and the user is told', async ($, on) => {
  const w = world(on, { failVersion: true })
  await start($)
  const out = (await ($ as Any).skill.prompt({ skill: 'devforgeai:brainstorm', text: CHECKLIST })) as Any
  expect(out).toEqual({ text: CHECKLIST })
  expect(runFiles(w, 'events.jsonl')).toEqual([])
  expect(w.toasts.some(t => t.startsWith('DevForgeAI progress: skill.prompt:'))).toBe(true)
})

// ---- After the plugin-validator's review ----

test('BEH-15: an interactive session that loads no tracked skill writes nothing in the project', async ($, on) => {
  const w = world(on)
  await start($)
  await load($, 'plugin-dev:create-plugin')
  await w.clock.advance(600)
  expect(w.writes).toEqual([])
})

test('BEH-15: the .gitignore is the first file written under devforgeai/progress/, and held log lines follow', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  const ours = w.writes.filter(p => p.startsWith(PROGRESS))
  expect(ours[0]).toBe(`${PROGRESS}/.gitignore`)
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes('mode: observe (framework-default)')).toBe(true)
})

test('BEH-04: two overlapping tool calls are both recorded, each with its own seq', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  await Promise.all([
    $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any),
    $.tool.call({ tool: 'Grep', pattern: 'x', path: `${ROOT}/docs` } as Any),
    $.tool.call({ tool: 'Glob', pattern: '*.md' } as Any),
  ])
  const lines = eventsOf(w)
  expect(lines.map(l => JSON.parse(l).seq)).toEqual([1, 2, 3, 4])
  expect(lines.slice(1).map(l => JSON.parse(l).tool).sort()).toEqual(['Glob', 'Grep', 'Read'])
})

test('VER-18 / VER-15 finding: the prompt that loads a skill arrives after the run opens, and is no answer', async ($, on) => {
  const w = world(on)
  await start($)
  // In a live session the slash command expands first: skill.prompt, then prompt.submit with the typed text.
  await load($)
  await ($ as Any).prompt.submit({ text: '/devforgeai:brainstorm a sticker. No questions asked', wait: false, origin: { kind: 'composer' } })
  await ($ as Any).prompt.submit({ text: 'yes, promote IDEA-03', wait: false, origin: { kind: 'composer' } })
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'prompt'])
})

// ---- SPEC-013 v3: the session's own files, a run's own root, pruning (VER-18) ----

function prunes(w: World): string[][] {
  return w.runs.filter(a => (a[1] ?? '').endsWith('/progress/prune.py'))
}

function argOf(argv: string[], flag: string): string {
  return argv[argv.indexOf(flag) + 1]
}

async function clearTo($: Any, w: World, id: string) {
  await $.session.end({ reason: 'clear', sessionId: w.sessionId, resume: { id: w.sessionId } })
  w.sessionId = id
  await $.classic.SessionStart({ source: 'clear' })
}

test('VER-18: current.json and adapter.log are the session\'s, and a new session ID starts a new folder', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  await w.clock.advance(600)
  expect(w.files.has(`${SESSION}/current.json`)).toBe(true)
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes('mode: observe (framework-default)')).toBe(true)
  await clearTo($, w, 's2')
  expect([...w.files.keys()].some(k => k.startsWith(`${PROGRESS}/sessions/s2/`))).toBe(false)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/b.md` } as Any)
  await w.clock.advance(600)
  expect(w.files.has(`${PROGRESS}/sessions/s2/current.json`)).toBe(true)
  expect((w.files.get(`${PROGRESS}/sessions/s2/adapter.log`) ?? '').includes('mode: observe (framework-default)')).toBe(true)
  expect(w.files.has(`${PROGRESS}/current.json`) || w.files.has(`${PROGRESS}/adapter.log`)).toBe(false)
})

test('VER-18: pruning starts once per session ID, after the first run\'s folder exists, with 30 days by default', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  await load($, 'devforgeai:architecture')
  await w.clock.advance(0)
  expect(prunes(w).length).toBe(1)
  const argv = prunes(w)[0]
  expect(argv.slice(2)).toEqual(['prune', '--root', ROOT, '--days', '30', '--keep-session', 's1', '--keep-run', argOf(argv, '--keep-run')])
  expect(argOf(argv, '--keep-run')).toMatch(/^20261002T120000Z-brainstorm-[0-9a-f]{8}$/)
  expect(w.pruneSawRun).toEqual([true])
  expect(w.inits[w.runs.indexOf(argv)].timeoutMs).toBe(10000)
  await clearTo($, w, 's2')
  await load($)
  await w.clock.advance(0)
  expect(prunes(w).map(a => argOf(a, '--keep-session'))).toEqual(['s1', 's2'])
})

test('VER-18: the retentionDays setting gives --days', { options: { retentionDays: 7 } } as Any, async ($: Any, on: Any) => {
  const w = world(on)
  await start($)
  await load($)
  await w.clock.advance(0)
  expect(argOf(prunes(w)[0], '--days')).toBe('7')
})

test('VER-18 / ERR-12: a prune that exits 2 leaves one adapter.log line, and tracking goes on', async ($, on) => {
  const w = world(on, { prune: () => ({ value: { exitCode: 2, stdout: '', stderr: 'prune: /work: not a folder\n' } }) })
  await start($)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  await w.clock.advance(600)
  const lines = (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' prune: '))
  expect(lines.length).toBe(1)
  expect(lines[0].endsWith('prune: prune: /work: not a folder')).toBe(true)
  expect(w.toasts.some(t => t.includes('prune'))).toBe(false)
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'tool'])
})

test('VER-18 / ERR-12: a prune that can\'t start leaves one adapter.log line and no notice', async ($, on) => {
  const w = world(on, { prune: () => ({ deny: 'failed to start: ENOENT' }) })
  await start($)
  await load($)
  await w.clock.advance(600)
  const lines = (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' prune: '))
  expect(lines.length).toBe(1)
  expect(lines[0].includes('failed to start')).toBe(true)
  expect(w.toasts.some(t => t.includes('prune') || t.includes('failed to start'))).toBe(false)
})

test('VER-18: no hook or tool call waits for pruning', async ($, on) => {
  let release: () => void = () => {}
  const w = world(on, {
    prune: () => new Promise(resolve => {
      release = () => resolve({ value: { exitCode: 0, stdout: 'pruned 1 runs, 0 sessions\n', stderr: '' } })
    }),
  })
  await start($)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'tool'])
  release()
  await w.clock.advance(600)
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes('prune: pruned 1 runs, 0 sessions')).toBe(true)
})

test('VER-18: a run opens under the root it reads, and the mode and pruning follow a new root', async ($, on) => {
  const w = world(on)
  const tree = `${ROOT}/.claude/worktrees/x`
  const treeProgress = `${tree}/devforgeai/progress`
  await start($)
  await load($)
  w.root = tree
  await load($, 'devforgeai:architecture')
  await $.tool.call({ tool: 'Write', file_path: `${tree}/docs/specs/arch/ARCH-001.md`, content: 'x' } as Any)
  await w.clock.advance(0)
  expect(w.files.get(`${treeProgress}/.gitignore`)).toBe('*\n')
  const log = [...w.files.keys()].find(k => k.startsWith(`${treeProgress}/runs/`) && k.endsWith('/events.jsonl')) ?? ''
  const events = (w.files.get(log) ?? '').split('\n').filter(Boolean).map(l => JSON.parse(l))
  expect(events.map(e => e.kind)).toEqual(['skill-loaded', 'tool'])
  expect(events[1].path).toBe('docs/specs/arch/ARCH-001.md')
  expect(JSON.parse(eventsOf(w).slice(-1)[0])).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  expect(w.runs.filter(a => a[2] === 'mode').map(a => argOf(a, '--root'))).toEqual([ROOT, tree])
  expect(prunes(w).map(a => argOf(a, '--root'))).toEqual([ROOT, tree])
  expect((w.files.get(`${treeProgress}/sessions/s1/adapter.log`) ?? '').includes('mode: observe (framework-default)')).toBe(true)
})

// ---- After the plugin-validator's review of version 3 ----

// A stored retentionDays under DM-06's floor never reaches the module: Claude Code refuses to load it ("options do
// not fit plugin.json userConfig: Keep progress files (days) must be at least 7"), so core.test.ts tests the clamp.

test('BEH-15: a session ID of another shape never makes a path; the run is still recorded', async ($, on) => {
  const w = world(on)
  w.sessionId = '../../outside'
  await start($)
  await load($)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  await w.clock.advance(600)
  expect(w.writes.every(p => !p.includes('..') && p.startsWith(`${PROGRESS}/`))).toBe(true)
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'tool'])
})

test('BEH-15: a .gitignore deleted during a run is written again before the next evaluation', async ($, on) => {
  const w = world(on)
  await start($)
  await load($)
  w.files.delete(`${PROGRESS}/.gitignore`)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  await w.clock.advance(600)
  expect(w.files.get(`${PROGRESS}/.gitignore`)).toBe('*\n')
})

// ---- version 4 and 5: the task list (BEH-20 to BEH-23) ----

const TAGGED = `${CHECKLIST}\nKeep this checklist in your task list: one task per step, subject '<N>. <title>', metadata devforgeai_step: N.`
const QUESTION = { tool: 'AskUserQuestion', questions: [{ question: 'Pick one?', header: 'Pick', options: [], multiSelect: false }] }
// SPEC-013 v6's text (VER-26): an earlier run's tasks don't count.
const QUESTION_REFUSAL = "DevForgeAI's progress tracker refused this question (enforce mode): no step of this run is marked in "
  + "progress in your task list. Tasks from an earlier run don't count: if this run's checklist isn't in your task list "
  + 'yet, turn it into tasks first as the skill says (one task per step, subject <N>. <title>, metadata '
  + 'devforgeai_step: N). Then mark the step this question belongs to in_progress (TaskUpdate, or TodoWrite), and ask '
  + 'again.'
// SPEC-013 v8 (BEH-21): an unmarked question with no step tag is also told to add the tag.
const QUESTION_TAG = ' Tag the question too: add metadata: {"source": "devforgeai_step:N"} to the AskUserQuestion call, N '
  + "being its step. If the question isn't part of this skill's checklist, give it a source of its own instead; it then "
  + 'needs no step and counts for none.'

/** Claude Code's task tools: TaskCreate numbers tasks in order; a few calls fail or answer without an ID. */
function taskTools() {
  let n = 0
  return (e: Any): Any => {
    if (e.tool === 'TaskCreate') {
      n += 1
      if (e.subject === '4. Wrap') return { result: { task: { id: String(n), subject: e.subject } }, text: 'Created.' }
      return { result: { task: { id: String(n), subject: e.subject } }, text: `Task #${n} created successfully: ${e.subject}` }
    }
    if (e.tool === 'TaskUpdate' && e.taskId === '2' && e.status === 'in_progress') {
      return { result: 'Error: update failed', isError: true, text: 'update failed' }
    }
    if (e.tool === 'TaskUpdate' && e.taskId === '99') return { result: 'Error: Task not found', isError: true, text: 'Task not found' }
    return bottomTool(e)
  }
}

function stepsOf(lines: string[]): Array<[number, string]> {
  return lines.map(l => JSON.parse(l)).filter(e => e.kind === 'step').map(e => [e.step, e.state])
}

test('VER-20: TaskCreate, TaskUpdate and TodoWrite give step events after their tool events', async ($, on) => {
  const w = world(on, { tool: taskTools() })
  await start($)
  await load($)
  await $.tool.call({ tool: 'TaskCreate', subject: '1. Intake', description: 'd', metadata: { devforgeai_step: 1 } } as Any)
  await $.tool.call({ tool: 'TaskCreate', subject: '2. Pick', description: 'd' } as Any)
  await $.tool.call({ tool: 'TaskCreate', subject: 'Tidy up', description: 'd' } as Any)
  await $.tool.call({ tool: 'TaskCreate', subject: '4. Wrap', description: 'd', metadata: { devforgeai_step: 4 } } as Any)
  await $.tool.call({ tool: 'TaskUpdate', taskId: '1', status: 'in_progress' } as Any)
  await $.tool.call({ tool: 'TaskUpdate', taskId: '1', status: 'completed' } as Any)
  await $.tool.call({ tool: 'TaskUpdate', taskId: '2', status: 'in_progress' } as Any)
  await $.tool.call({ tool: 'TaskUpdate', taskId: '99', status: 'in_progress' } as Any)
  await $.tool.call({ tool: 'TaskUpdate', taskId: '2', status: 'pending' } as Any)
  await $.tool.call({ tool: 'TodoWrite', todos: [
    { content: '5. Draft', status: 'in_progress', activeForm: 'Drafting' },
    { content: '6. Check', status: 'completed', activeForm: 'Checking' },
    { content: 'Notes', status: 'in_progress', activeForm: 'Noting' },
  ] } as Any)
  const lines = eventsOf(w)
  expect(JSON.parse(lines[0]).taskList).toBe(true)
  expect(lines.map(l => { const e = JSON.parse(l); return e.kind === 'tool' ? e.tool : e.kind === 'step' ? `step ${e.step} ${e.state}` : e.kind })).toEqual([
    'skill-loaded', 'TaskCreate', 'TaskCreate', 'TaskCreate', 'TaskCreate',
    'TaskUpdate', 'step 1 started', 'TaskUpdate', 'step 1 done', 'TaskUpdate', 'TaskUpdate', 'TaskUpdate',
    'TodoWrite', 'step 5 started', 'step 6 done',
  ])
  const log = w.files.get(`${SESSION}/adapter.log`) ?? ''
  expect(log.split('\n').filter(l => l.includes(' task: ')).length).toBe(2)
  expect(log.includes('Tidy up')).toBe(true)
  expect(log.includes('4. Wrap')).toBe(true)
})

test('VER-20: a new run starts with an empty task map, and a TodoWrite step already done gives nothing more', async ($, on) => {
  const w = world(on, { tool: taskTools() })
  await start($)
  await load($)
  await $.tool.call({ tool: 'TaskCreate', subject: '1. Intake', description: 'd' } as Any)
  await $.tool.call({ tool: 'TodoWrite', todos: [{ content: '2. Pick', status: 'completed', activeForm: 'x' }] } as Any)
  await $.tool.call({ tool: 'TodoWrite', todos: [{ content: '2. Pick', status: 'completed', activeForm: 'x' }] } as Any)
  expect(stepsOf(eventsOf(w))).toEqual([[2, 'done']])
  await load($)
  await $.tool.call({ tool: 'TaskUpdate', taskId: '1', status: 'in_progress' } as Any)
  expect(stepsOf(eventsOf(w))).toEqual([])
})

function questioningAt(seq: number, refuse = true) {
  return (argv: readonly string[]) => (argv.some(a => a.endsWith('/pending.jsonl'))
    ? { state: { ...STATE, gate: { kind: 'question', seq, refuse, reason: 'x' },
      flags: refuse ? [{ gate: 'question', seq, step: 2, type: 'unmarked-question', message: 'a question was asked while no step was marked in progress in the task list: mark the step it belongs to in progress, then ask' }] : [] } }
    : { state: STATE })
}

test('VER-21: in enforce mode a question at the question gate is refused before it runs, and recorded as nothing', async ($, on) => {
  const w = world(on, { mode: 'enforce local', evaluate: questioningAt(2), tool: bottomTool })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  const r = (await $.tool.call(QUESTION as Any)) as Any
  expect(r.deny).toBe(QUESTION_REFUSAL + QUESTION_TAG)
  expect(w.tools.includes('AskUserQuestion')).toBe(false)
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded'])
  expect(w.writes.filter(p => p.endsWith('/pending.jsonl')).length).toBe(1)
  expect(JSON.parse((w.files.get(w.writes.filter(p => p.endsWith('/pending.jsonl'))[0]) ?? '').trim().split('\n').slice(-1)[0]))
    .toMatchObject({ seq: 2, kind: 'answer', answered: true })
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes(' refused: ')).toBe(true)
  expect(w.toasts.filter(t => t === QUESTION_REFUSAL + QUESTION_TAG).length).toBe(1)
})

test('VER-21: with refuse false or a gate at another seq the question goes on and its answer is recorded', async ($, on) => {
  let calls = 0
  const w = world(on, { mode: 'enforce local', tool: bottomTool,
    evaluate: argv => (calls++ % 2 === 0 ? questioningAt(2, false)(argv) : questioningAt(9)(argv)) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  const r1 = (await $.tool.call(QUESTION as Any)) as Any
  const r2 = (await $.tool.call(QUESTION as Any)) as Any
  expect(r1.deny).toBeUndefined()
  expect(r2.deny).toBeUndefined()
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'answer', 'answer'])
  expect(w.writes.filter(p => p.endsWith('/pending.jsonl')).length).toBe(2)
})

test('VER-21: observe mode checks no question; a run that doesn\'t follow the task list is checked and refused nothing', async ($, on) => {
  const w = world(on, { tool: bottomTool, evaluate: questioningAt(2) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  const r = (await $.tool.call(QUESTION as Any)) as Any
  expect(r.deny).toBeUndefined()
  expect(w.writes.some(p => p.endsWith('/pending.jsonl'))).toBe(false)
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'answer'])
})

test('VER-21: in enforce mode an untagged run\'s question is checked and goes on', async ($, on) => {
  const w = world(on, { mode: 'enforce local', tool: bottomTool })
  await start($)
  await load($)
  const r = (await $.tool.call(QUESTION as Any)) as Any
  expect(r.deny).toBeUndefined()
  expect(w.writes.filter(p => p.endsWith('/pending.jsonl')).length).toBe(1)
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'answer'])
})

test('VER-21: a mod\'s own question is never checked', { plugins: [asker] }, async ($, on) => {
  const w = world(on, { mode: 'enforce local', evaluate: questioningAt(2), tool: bottomTool })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await ($ as Any).command.run({ command: 'ask-me', args: '' })
  expect(w.writes.some(p => p.endsWith('/pending.jsonl'))).toBe(false)
})

const ADHERENCE = (n: number, m: number) => `brainstorm didn't keep its task list: ${n} step events, ${m} questions asked without `
  + 'their step marked and tagged. Recommended: fix the skill so it keeps its checklist in the task list (DevForgeAI SPEC-012 §4)'

function stateWith(gate: string | null, ended: string | null, stepEvents: number, unmarkedQuestions: number) {
  return () => ({ state: { ...STATE, ended, current: ended === null ? STATE.current : null,
    gate: { kind: gate, seq: gate === null ? null : 3, refuse: false, reason: null },
    counts: { stepEvents, unmarkedQuestions } } })
}

for (const mode of ['observe framework-default', 'enforce local']) {
  test(`VER-23: a run that follows the task list and didn't keep it gets one adherence toast (${mode.split(' ')[0]})`, async ($, on) => {
    const w = world(on, { mode, evaluate: stateWith('report', null, 4, 2) })
    await start($)
    await load($, 'devforgeai:brainstorm', TAGGED)
    await w.clock.advance(600)
    await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
    await w.clock.advance(600)
    expect(w.toasts.filter(t => t === ADHERENCE(4, 2)).length).toBe(1)
    const log = w.files.get(`${SESSION}/adapter.log`) ?? ''
    expect(log.split('\n').filter(l => l.includes(` adherence: ${ADHERENCE(4, 2)}`)).length).toBe(1)
  })
}

test('VER-23: a run that ends with no step event gets the toast too', async ($, on) => {
  const w = world(on, { evaluate: stateWith('end', 'session-end', 0, 0) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await w.clock.advance(600)
  expect(w.toasts.filter(t => t === ADHERENCE(0, 0)).length).toBe(1)
})

test('VER-23: no adherence toast for a run that keeps its list, or one that doesn\'t follow the task list', async ($, on) => {
  const w = world(on, { evaluate: stateWith('report', null, 6, 0) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await w.clock.advance(600)
  await load($)
  await w.clock.advance(600)
  expect(w.toasts.some(t => t.includes("didn't keep its task list"))).toBe(false)
})

test('VER-23: a run without the tag ending with no step event gets no adherence toast', async ($, on) => {
  const w = world(on, { evaluate: stateWith('end', 'session-end', 0, 0) })
  await start($)
  await load($)
  await w.clock.advance(600)
  expect(w.toasts.some(t => t.includes("didn't keep its task list"))).toBe(false)
})

const TOOL_LISTS: Array<[string, string[] | 'reject', boolean]> = [
  ['TaskCreate and TaskUpdate', ['Read', 'TaskCreate', 'TaskUpdate'], true],
  ['TodoWrite alone', ['Read', 'TodoWrite'], true],
  ['TaskStop and ToolSearch only', ['Read', 'TaskStop', 'ToolSearch'], false],
  ['a rejected tool list', 'reject', false],
]

for (const [name, toolList, expected] of TOOL_LISTS) {
  test(`VER-24: ${name} gives skill-loaded taskList ${expected}`, async ($, on) => {
    const w = world(on, { toolList })
    await start($)
    await load($, 'devforgeai:brainstorm', TAGGED)
    expect(JSON.parse(eventsOf(w)[0]).taskList).toBe(expected)
    const tools = (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' tools: '))
    expect(tools.length).toBe(toolList === 'reject' ? 1 : 0)
    // A list that can't be read shows nothing (ERR-14): the hint would blame tools the session may well have.
    if (toolList === 'reject') expect(w.toasts.some(t => t.includes('has no task list'))).toBe(false)
  })
}

test('VER-24: without task tools, enforce mode lets a question go on and shows no adherence toast', async ($, on) => {
  const w = world(on, { mode: 'enforce local', toolList: ['Read'], tool: bottomTool, evaluate: stateWith('report', null, 0, 0) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  const r = (await $.tool.call(QUESTION as Any)) as Any
  await w.clock.advance(600)
  expect(r.deny).toBeUndefined()
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded', 'answer'])
  expect(w.toasts.some(t => t.includes("didn't keep its task list"))).toBe(false)
})

const HINT = 'brainstorm: this session has no task list, so DevForgeAI places your answers by guessing. For exact step '
  + 'tracking, start Claude Code with CLAUDE_CODE_ENABLE_TODO_TOOLS=1 (DevForgeAI SPEC-012 §4)'

for (const mode of ['observe framework-default', 'enforce local']) {
  test(`VER-25: a session without task tools is told once (${mode.split(' ')[0]})`, async ($, on) => {
    const w = world(on, { mode, toolList: ['Read', 'TaskStop'] })
    await start($)
    await load($, 'devforgeai:brainstorm', TAGGED)
    await load($, 'devforgeai:brainstorm', TAGGED)
    expect(w.toasts.filter(t => t === HINT).length).toBe(1)
    const log = w.files.get(`${SESSION}/adapter.log`) ?? ''
    expect(log.split('\n').filter(l => l.includes(' tools-hint: ')).length).toBe(1)
  })
}

test('VER-25: no hint with the task tools, or for a skill whose text doesn\'t name the tag', async ($, on) => {
  const w = world(on, { toolList: ['Read'] })
  await start($)
  await load($)
  expect(w.toasts.some(t => t.includes('has no task list'))).toBe(false)
})

test('VER-25: no hint when the session has the task tools', async ($, on) => {
  const w = world(on)
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  expect(w.toasts.some(t => t.includes('has no task list'))).toBe(false)
})

// ---- after the build's plugin-validator review ----

/** The evaluator, as a question check sees it: a pending log with a step event refuses nothing, one without refuses. */
function refusingUnlessStarted(files: () => Map<string, string>) {
  return (argv: readonly string[]) => {
    const events = argv[argv.indexOf('--events') + 1]
    if (!events.endsWith('/pending.jsonl')) return { state: STATE }
    const lines = (files().get(events) ?? '').trim().split('\n').map(l => JSON.parse(l))
    if (lines.some(e => e.kind === 'step' && e.state === 'started')) return { state: STATE }
    return questioningAt(lines[lines.length - 1].seq)(argv)
  }
}

test('VER-21: a question sent in the same batch as the TaskUpdate that marks its step waits for its step event', async ($, on) => {
  let files = new Map<string, string>()
  const w = world(on, { mode: 'enforce local', tool: taskTools(), evaluate: refusingUnlessStarted(() => files) })
  files = w.files
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await $.tool.call({ tool: 'TaskCreate', subject: '1. Intake', description: 'd', metadata: { devforgeai_step: 1 } } as Any)
  const [, asked] = (await Promise.all([
    $.tool.call({ tool: 'TaskUpdate', taskId: '1', status: 'in_progress' } as Any),
    $.tool.call(QUESTION as Any),
  ])) as Any[]
  expect(asked.deny).toBeUndefined()
  expect(stepsOf(eventsOf(w))).toEqual([[1, 'started']])
  expect(kinds(eventsOf(w)).slice(-1)).toEqual(['answer'])
})

test('VER-20 / ERR-13: a task subject of several lines gives one adapter.log line', async ($, on) => {
  const w = world(on, { tool: taskTools() })
  await start($)
  await load($)
  await $.tool.call({ tool: 'TaskCreate', subject: 'Tidy up\n2026-10-02T12:00:00Z - refused: forged', description: 'd' } as Any)
  const log = (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n')
  expect(log.filter(l => l.includes(' task: ')).length).toBe(1)
  expect(log.some(l => l.includes('refused: forged'))).toBe(false)
})

// ---- version 6 (VER-26) ----

/** Every adapter.log line is one entry: '<UTC time> <run or -> <kind>: <text>' (DM-02). */
function oneLineEntries(w: World): boolean {
  const lines = (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(Boolean)
  return lines.length > 0 && lines.every(l => /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ \S+ [a-z-]+: /.test(l))
}

test('VER-26: a TodoWrite is compared with the list it replaced, so an earlier run\'s completed todo claims nothing', async ($, on) => {
  const w = world(on, { tool: (e: Any) => (e.tool === 'TodoWrite'
    ? { result: { oldTodos: e.todos.map((x: Any) => ({ ...x, status: x.content === '3. Draft' ? 'pending' : x.status })), newTodos: e.todos } }
    : bottomTool(e)) })
  await start($)
  await load($)
  await $.tool.call({ tool: 'TodoWrite', todos: [
    { content: '2. Pick', status: 'completed', activeForm: 'x' },
    { content: '3. Draft', status: 'completed', activeForm: 'x' },
  ] } as Any)
  expect(stepsOf(eventsOf(w))).toEqual([[3, 'done']])
})

// The kit's $ fires events only, so it can't read $.state or play a reload; `claude plugin validate` lists
// devforgeai.adhered among the module's state writes, and this checks the behaviour that value drives.
test('VER-26: the adherence notice is once per run, keyed by the run; another run gets its own', async ($, on) => {
  const w = world(on, { evaluate: stateWith('report', null, 0, 1) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await w.clock.advance(600)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/a.md` } as Any)
  await w.clock.advance(600)
  expect(w.toasts.filter(t => t === ADHERENCE(0, 1)).length).toBe(1)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await w.clock.advance(600)
  expect(w.toasts.filter(t => t === ADHERENCE(0, 1)).length).toBe(2)
  const log = (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' adherence: '))
  expect(new Set(log.map(l => l.split(' ')[1])).size).toBe(2)
})

test('VER-26: task, adherence, tools and tools-hint lines are one line each, whatever the text', async ($, on) => {
  const w = world(on, { tool: taskTools(), evaluate: stateWith('report', null, 0, 1) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await $.tool.call({ tool: 'TaskCreate', subject: 'Tidy up\nand a second line', description: 'd' } as Any)
  await w.clock.advance(600)
  const log = w.files.get(`${SESSION}/adapter.log`) ?? ''
  expect(log.includes(' task: ')).toBe(true)
  expect(log.includes(' adherence: ')).toBe(true)
  expect(oneLineEntries(w)).toBe(true)
})

test('VER-26: the tools and tools-hint lines are one line each', async ($, on) => {
  const w = world(on, { toolList: 'reject' })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes(' tools: ')).toBe(true)
  expect(oneLineEntries(w)).toBe(true)
})

test('VER-26: the tools-hint line is one line', async ($, on) => {
  const w = world(on, { toolList: ['Read'] })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes(' tools-hint: ')).toBe(true)
  expect(oneLineEntries(w)).toBe(true)
})

// ---- version 7 (VER-28): the compaction hook; version 7's VER-27 was replaced by version 8's VER-31 ----

const DECISION_STEPS = [
  { n: 1, title: 'Intake', state: 'done' },
  { n: 2, title: 'Pick', state: 'done' },
  { n: 8, title: 'Propose and confirm the outcome', state: 'skipped', userOwned: true },
  { n: 9, title: 'Write the ARCH and ADRs', state: 'rule-broken' },
]

/** A provisional state refusing the pending Write over step 8's decision (a user-owned skipped flag and step 9's
 *  rule-broken one), at the pending event's own seq; `owned` false leaves the user-owned flag out. */
function refusingDecision(files: () => Map<string, string>, owned = true) {
  return (argv: readonly string[]) => {
    const events = argv[argv.indexOf('--events') + 1]
    if (!events.endsWith('/pending.jsonl')) return { state: STATE }
    const lines = (files().get(events) ?? '').trim().split('\n')
    const seq = JSON.parse(lines[lines.length - 1]).seq
    const flags = [
      ...(owned ? [{ gate: 'write', seq, step: 8, type: 'skipped', message: 'step 8 (Propose and confirm the outcome) had no answer from you before docs/specs/arch/ARCH-001.md was written' }] : []),
      { gate: 'write', seq, step: 9, type: 'rule-broken', message: 'docs/specs/arch/ARCH-001.md sets outcome: create, which needs your answer at step 8' },
    ]
    return { state: { ...STATE, steps: DECISION_STEPS, gate: { kind: 'write', seq, refuse: true, reason: 'x' }, flags } }
  }
}

const ARCH_WRITE = { tool: 'Write', file_path: `${ROOT}/docs/specs/arch/ARCH-001.md`, content: 'outcome: create' }

async function markStep($: Any, n: number, title: string) {
  await $.tool.call({ tool: 'TaskCreate', subject: `${n}. ${title}`, description: 'd', metadata: { devforgeai_step: n } } as Any)
  // taskTools() numbers tasks in creation order; each test creates only the one task it marks.
  await $.tool.call({ tool: 'TaskUpdate', taskId: '1', status: 'in_progress' } as Any)
}

// A compaction always holds at least one message: the engine refuses a hook's rewrite with none.
const TALK = [{ role: 'user', text: 'earlier talk', toolUses: [] }]

const NOTE = (step: string) => `DevForgeAI's progress tracker: when this conversation was compacted, your task list marked ${step} in `
  + "progress. Before you ask anything or go on, check your task list and bring it in step with the work: mark each "
  + "finished step done and the step you're on in_progress."

test('VER-28: a compaction keeps the marked step in the summary and ends with the note', async ($, on) => {
  const w = world(on, { tool: taskTools() })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await markStep($, 2, 'Pick')
  await w.clock.advance(600)
  const out = (await ($ as Any).session.compact({ trigger: 'manual', instructions: 'keep the plan', messages: TALK })) as Any
  expect(w.compactIn[0].instructions).toBe("keep the plan\n\nKeep, for DevForgeAI's progress tracker: in the architecture run, the task list marks step 2 (Pick) in progress.")
  expect(out.messages.map((m: Any) => m.text)).toEqual(['the summary', NOTE('step 2 (Pick)')])
  expect(out.messages[1].role).toBe('user')
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes(' compact: ')).toBe(true)
})

test('VER-28: with no step marked the note says so', async ($, on) => {
  const w = world(on, { tool: taskTools() })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  const out = (await ($ as Any).session.compact({ trigger: 'auto', messages: TALK })) as Any
  expect(w.compactIn[0].instructions).toBe("Keep, for DevForgeAI's progress tracker: in the architecture run, the task list marks no step in progress.")
  expect(out.messages.map((m: Any) => m.text)).toEqual(['the summary', NOTE('no step')])
})

test('VER-28: a run that does not follow the task list, a subagent\'s compaction and a skipped one pass unchanged', async ($, on) => {
  let skip = false
  const w = world(on, { tool: taskTools(), compact: () => (skip ? { skip: 'off' } : undefined) })
  await start($)
  await load($, 'devforgeai:architecture', CHECKLIST)
  const plain = (await ($ as Any).session.compact({ trigger: 'manual', instructions: 'keep', messages: TALK })) as Any
  expect(w.compactIn[0].instructions).toBe('keep')
  expect(plain.messages.map((m: Any) => m.text)).toEqual(['the summary'])
  await load($, 'devforgeai:architecture', TAGGED)
  const sub = (await ($ as Any).session.compact({ trigger: 'manual', agentId: 'a1', messages: TALK })) as Any
  expect(w.compactIn[1].instructions).toBeUndefined()
  expect(sub.messages.map((m: Any) => m.text)).toEqual(['the summary'])
  skip = true
  const skipped = (await ($ as Any).session.compact({ trigger: 'manual', messages: TALK })) as Any
  expect(skipped).toEqual({ skip: 'off' })
})

// ---- version 8 (VER-30 to VER-33): questions name their step ----

const MESSAGES: Record<string, string> = {
  'unmarked-question': 'a question was asked while no step was marked in progress in the task list: mark the step it belongs to in progress, then ask',
  'untagged-question': 'a question was asked without naming a step of the checklist: tag it with devforgeai_step:N for the step it belongs to, mark that step in progress, then ask',
  'mismatched-question': 'a question for step 5 was asked while step 2 was marked in progress: mark step 5 in progress, then ask',
}
const MISMATCHED = "DevForgeAI's progress tracker refused this question (enforce mode): it is tagged for step 5, but your task "
  + 'list marks step 2 in progress. If the question belongs to step 5, mark step 5 in_progress (TaskUpdate, or TodoWrite) '
  + 'and ask again; if it belongs to step 2, tag it devforgeai_step:2 and ask again.'
const UNTAGGED = "DevForgeAI's progress tracker refused this question (enforce mode): it doesn't name a step of this skill's "
  + 'checklist. Add metadata: {"source": "devforgeai_step:N"} to the AskUserQuestion call, N being the step it belongs '
  + 'to; your task list marks step 2 in progress, so if the question belongs to another step, mark that step '
  + "in_progress first. If the question isn't part of this skill's checklist, give it a source of its own instead; it "
  + 'then counts for no step. Then ask again.'

function asked(source?: unknown) {
  return { ...QUESTION, ...(source === undefined ? {} : { metadata: { source } }) }
}

/** The pending file's last event: the event an enforce check judged. */
function pendingEvent(w: World): Any {
  const path = w.writes.filter(p => p.endsWith('/pending.jsonl')).slice(-1)[0]
  const lines = (w.files.get(path) ?? '').trim().split('\n')
  return JSON.parse(lines[lines.length - 1])
}

/** A provisional state whose question gate raises a flag of `type` for `step` at the pending event's own seq. */
function questionGate(files: () => Map<string, string>, type: string, step: number) {
  return (argv: readonly string[]) => {
    const events = argv[argv.indexOf('--events') + 1]
    if (!events.endsWith('/pending.jsonl')) return { state: STATE }
    const lines = (files().get(events) ?? '').trim().split('\n')
    const seq = JSON.parse(lines[lines.length - 1]).seq
    // The checklist has step 5, the step the tagged questions name; step 40 stays unknown (review N3).
    return { state: { ...STATE, steps: [...STATE.steps, { n: 5, title: 'Confirm', state: 'pending' }],
      gate: { kind: 'question', seq, refuse: true, reason: MESSAGES[type] },
      flags: [{ gate: 'question', seq, step, type, message: MESSAGES[type] }] } }
  }
}

test('VER-30: an answer event carries the step its question\'s metadata names, or outside for another source', async ($, on) => {
  const w = world(on, { tool: bottomTool })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  for (const source of ['devforgeai_step:5', 'devforgeai_step:0', 'devforgeai_step:05', 'devforgeai_step: 5', 'remember', 7]) {
    await $.tool.call(asked(source) as Any)
  }
  await $.tool.call(asked() as Any)
  const answers = eventsOf(w).map(l => JSON.parse(l)).filter(e => e.kind === 'answer')
  expect(answers.map(e => [e.step ?? null, e.outside ?? null])).toEqual([
    [5, null], [null, null], [null, null], [null, null], [null, true], [null, null], [null, null],
  ])
})

for (const [name, type, step, source, mark, expected] of [
  ['an unmarked question with no tag', 'unmarked-question', 2, undefined, false, QUESTION_REFUSAL + QUESTION_TAG],
  ['an unmarked question with a tag', 'unmarked-question', 5, 'devforgeai_step:5', false, QUESTION_REFUSAL],
  ['a question tagged for another step', 'mismatched-question', 5, 'devforgeai_step:5', true, MISMATCHED],
  ['a question with no tag while a step is marked', 'untagged-question', 2, undefined, true, UNTAGGED],
] as Array<[string, string, number, string | undefined, boolean, string]>) {
  test(`VER-30: the refusal of ${name} says which fix applies`, async ($, on) => {
    let files = new Map<string, string>()
    const w = world(on, { mode: 'enforce local', tool: taskTools(), evaluate: questionGate(() => files, type, step) })
    files = w.files
    await start($)
    await load($, 'devforgeai:brainstorm', TAGGED)
    if (mark) await markStep($, 2, 'Pick')
    const r = (await $.tool.call(asked(source) as Any)) as Any
    expect(r.deny).toBe(expected)
    expect(pendingEvent(w)).toMatchObject({ kind: 'answer', answered: true })
    expect(pendingEvent(w).step).toBe(source === undefined ? undefined : 5)
  })
}

test('VER-30: the pending event of a question with another source carries outside', async ($, on) => {
  const w = world(on, { mode: 'enforce local', tool: bottomTool })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  const r = (await $.tool.call(asked('remember') as Any)) as Any
  expect(r.deny).toBeUndefined()
  expect(pendingEvent(w)).toMatchObject({ kind: 'answer', answered: true, outside: true })
})

// VER-31: the write refusal's line for a decision, whichever step is marked, and the toasts written for the user.
const LINE8 = "Step 8 (Propose and confirm the outcome) is the user's decision: an answer counts for it only while step 8 is "
  + 'marked in progress, and an answer to a question only when the question is also tagged devforgeai_step:8. Mark step 8 '
  + 'in_progress, ask the user with the question tagged devforgeai_step:8, and mark step 8 completed.'
const TYPED2 = 'Your task list marks step 2 (Pick) in progress, so what the user typed since then counted for step 2.'

async function typed($: Any, text = 'go on') {
  await $.prompt.submit({ text, wait: false, origin: { kind: 'composer' } })
}

for (const [name, setup, opts, lines] of [
  ['a later step (9) is marked', async ($: Any) => markStep($, 9, 'Write the ARCH and ADRs'), {}, [LINE8]],
  ['no step is marked', async () => {}, {}, [LINE8]],
  ['step 2 is marked and the user typed since', async ($: Any) => { await markStep($, 2, 'Pick'); await typed($) }, {}, [TYPED2, LINE8]],
  ['step 2 is marked and only a question was answered since', async ($: Any) => { await markStep($, 2, 'Pick'); await $.tool.call(QUESTION) }, {}, [LINE8]],
  ['step 8 itself is marked', async ($: Any) => markStep($, 8, 'Propose and confirm the outcome'), {}, []],
  ['no user-owned step was skipped', async ($: Any) => markStep($, 2, 'Pick'), { owned: false }, []],
  ['the run does not follow the task list', async ($: Any) => { await markStep($, 2, 'Pick'); await typed($) }, { untagged: true }, []],
] as Array<[string, ($: Any) => Promise<void>, Any, string[]]>) {
  test(`VER-31: the write refusal when ${name}`, async ($, on) => {
    let files = new Map<string, string>()
    const w = world(on, { mode: 'enforce local', tool: taskTools(), evaluate: refusingDecision(() => files, opts.owned !== false) })
    files = w.files
    await start($)
    await load($, 'devforgeai:architecture', opts.untagged ? CHECKLIST : TAGGED)
    await setup($)
    const r = (await $.tool.call(ARCH_WRITE as Any)) as Any
    expect(typeof r.deny).toBe('string')
    const said = (r.deny as string).split('\n')
    expect(said.filter(l => l === LINE8 || l === TYPED2)).toEqual(lines)
    expect(said.some(l => l.startsWith('Your task list marks') && l !== TYPED2)).toBe(false)
  })
}

/** A state with step 8's decision skipped at seq 99, after everything the test records, and a question gate's flag. */
const OBSERVED = { ...STATE, skill: 'architecture', steps: DECISION_STEPS, gate: { kind: 'write', seq: 99, refuse: true, reason: 'x' },
  flags: [
    { gate: 'write', seq: 99, step: 8, type: 'skipped', message: 'step 8 had no answer from you' },
    { gate: 'question', seq: 98, step: 2, type: 'untagged-question', message: MESSAGES['untagged-question'] },
  ] }
const USER_SENTENCE = 'What you typed since step 2 (Pick) was marked in progress counted for step 2: the architecture skill '
  + "didn't keep its task list in step with its work (DevForgeAI SPEC-012 §4)."

test('VER-31: in observe mode the decision\'s toast tells the user where typed answers went', async ($, on) => {
  const w = world(on, { tool: taskTools(), evaluate: () => ({ state: OBSERVED }) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await markStep($, 2, 'Pick')
  await typed($)
  await w.clock.advance(600)
  expect(w.toasts.filter(t => t === `✗ Step 8 skipped: step 8 had no answer from you ${USER_SENTENCE}`).length).toBe(1)
  expect(w.toasts.filter(t => t === `✗ Step 2 untagged-question: ${MESSAGES['untagged-question']} Its answer, if any, counts for no step.`).length).toBe(1)
})

test('VER-31: with no typed answer since the mark, the decision\'s toast has no sentence', async ($, on) => {
  const w = world(on, { tool: taskTools(), evaluate: () => ({ state: OBSERVED }) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await markStep($, 2, 'Pick')
  await $.tool.call(QUESTION as Any)
  await w.clock.advance(600)
  expect(w.toasts.filter(t => t === '✗ Step 8 skipped: step 8 had no answer from you').length).toBe(1)
})

// VER-32: the same refusal twice in a run tells the user once; since version 9 its last sentence follows the cause.
const TASK_ADVICE = "Help Claude bring its task list in step, or switch to observe mode with the band's button."
const EVIDENCE_ADVICE = "Claude hasn't done that step in a way the tracker can see: ask Claude to do it as the message "
  + "says, or switch to observe mode with the band's button."
const DECISION_ADVICE = "The refused write records a decision that needs your answer: answer Claude's question about it, "
  + "or ask Claude to leave it open, or switch to observe mode with the band's button."
const STUCK = (skill: string, step: number, message: string, advice = TASK_ADVICE) => `${skill}: the progress tracker `
  + `refused Claude twice at step ${step} for the same reason: ${message}. ${advice}`

function stuckLines(w: World): string[] {
  return (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' stuck: '))
}

test('VER-32: two question refusals for the same cause tell the user once; a third tells nothing more', async ($, on) => {
  let files = new Map<string, string>()
  const w = world(on, { mode: 'enforce local', tool: bottomTool, evaluate: questionGate(() => files, 'unmarked-question', 2) })
  files = w.files
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  const notice = STUCK('brainstorm', 2, MESSAGES['unmarked-question'])
  await $.tool.call(QUESTION as Any)
  expect(w.toasts.filter(t => t === notice).length).toBe(0)
  await $.tool.call(QUESTION as Any)
  expect(w.toasts.filter(t => t === notice).length).toBe(1)
  await $.tool.call(QUESTION as Any)
  expect(w.toasts.filter(t => t === notice).length).toBe(1)
  expect(stuckLines(w).length).toBe(1)
  expect(oneLineEntries(w)).toBe(true)
})

for (const [type, step, question] of [
  ['untagged-question', 2, QUESTION],
  ['mismatched-question', 5, asked('devforgeai_step:5')],
] as Array<[string, number, Any]>) {
  test(`VER-32 (version 9): two ${type} refusals end with the task-list advice`, async ($, on) => {
    let files = new Map<string, string>()
    const w = world(on, { mode: 'enforce local', tool: taskTools(), evaluate: questionGate(() => files, type, step) })
    files = w.files
    await start($)
    await load($, 'devforgeai:brainstorm', TAGGED)
    await $.tool.call(question as Any)
    await $.tool.call(question as Any)
    expect(w.toasts.filter(t => t === STUCK('brainstorm', step, MESSAGES[type])).length).toBe(1)
    expect(w.toasts.some(t => t.includes(EVIDENCE_ADVICE) || t.includes(DECISION_ADVICE))).toBe(false)
  })
}

test('VER-32: refusals for different causes, or one in each of two runs, tell nothing', async ($, on) => {
  let files = new Map<string, string>()
  let type = 'unmarked-question'
  const w = world(on, { mode: 'enforce local', tool: taskTools(),
    evaluate: argv => questionGate(() => files, type, type === 'unmarked-question' ? 2 : 5)(argv) })
  files = w.files
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await $.tool.call(QUESTION as Any)
  type = 'mismatched-question'
  await $.tool.call(asked('devforgeai_step:5') as Any)
  type = 'unmarked-question'
  await load($, 'devforgeai:brainstorm', TAGGED)
  await $.tool.call(QUESTION as Any)
  expect(w.toasts.some(t => t.includes('refused Claude twice'))).toBe(false)
  expect(stuckLines(w).length).toBe(0)
})

test('VER-32: two write refusals for the same cause tell the user too; observe mode tells nothing', async ($, on) => {
  let files = new Map<string, string>()
  const w = world(on, { mode: 'enforce local', tool: taskTools(), evaluate: refusingDecision(() => files) })
  files = w.files
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await $.tool.call(ARCH_WRITE as Any)
  await $.tool.call(ARCH_WRITE as Any)
  const notice = STUCK('architecture', 8, 'step 8 (Propose and confirm the outcome) had no answer from you before docs/specs/arch/ARCH-001.md was written', DECISION_ADVICE)
  expect(w.toasts.filter(t => t === notice).length).toBe(1)
  expect(stuckLines(w).length).toBe(1)
})

test('VER-32 (version 9): a write refused for its rule-broken flag alone gets the decision advice too', async ($, on) => {
  let files = new Map<string, string>()
  const w = world(on, { mode: 'enforce local', tool: taskTools(), evaluate: refusingDecision(() => files, false) })
  files = w.files
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await $.tool.call(ARCH_WRITE as Any)
  await $.tool.call(ARCH_WRITE as Any)
  const notice = STUCK('architecture', 9, 'docs/specs/arch/ARCH-001.md sets outcome: create, which needs your answer at step 8', DECISION_ADVICE)
  expect(w.toasts.filter(t => t === notice).length).toBe(1)
  expect(stuckLines(w).length).toBe(1)
})

/** A provisional state refusing the pending Write for one flag of `type` on `step`, a step that isn't user-owned
 *  (DECISION_STEPS lists steps 1, 2, 8 and 9; only 8 is user-owned). */
function refusingStep(files: () => Map<string, string>, type: string, step: number, message: string) {
  return (argv: readonly string[]) => {
    const events = argv[argv.indexOf('--events') + 1]
    if (!events.endsWith('/pending.jsonl')) return { state: STATE }
    const lines = (files().get(events) ?? '').trim().split('\n')
    const seq = JSON.parse(lines[lines.length - 1]).seq
    return { state: { ...STATE, steps: DECISION_STEPS, gate: { kind: 'write', seq, refuse: true, reason: message },
      flags: [{ gate: 'write', seq, step, type, message }] } }
  }
}

for (const [name, type, step, message] of [
  ['a ticked step whose script run wasn\'t seen (live run 2)', 'claimed-not-evidenced', 1,
    "step 1 (Resolve policy (R1, R2)) is ticked, but a successful run of validate_policy.py wasn't seen"],
  ['a script run joined to another command', 'skipped', 1,
    'step 1 (Resolve policy (R1, R2)): validate_policy.py ran, but the command joined it to another, which hides its exit status: run it as a command of its own'],
  ['a skipped step the state doesn\'t list', 'skipped', 3,
    'step 3 (Read the PRD) has no evidence or tick before the write gate: expected a read of docs/specs/prd/PRD-*.md'],
] as Array<[string, string, number, string]>) {
  test(`VER-32 (version 9): ${name} gets the evidence advice`, async ($, on) => {
    let files = new Map<string, string>()
    const w = world(on, { mode: 'enforce local', tool: taskTools(), evaluate: refusingStep(() => files, type, step, message) })
    files = w.files
    await start($)
    await load($, 'devforgeai:architecture', TAGGED)
    await $.tool.call(ARCH_WRITE as Any)
    await $.tool.call(ARCH_WRITE as Any)
    expect(w.toasts.filter(t => t === STUCK('architecture', step, message, EVIDENCE_ADVICE)).length).toBe(1)
    expect(w.toasts.some(t => t.includes(TASK_ADVICE) || t.includes(DECISION_ADVICE))).toBe(false)
    expect(stuckLines(w).length).toBe(1)
  })
}

test('VER-32: observe mode refuses nothing, so it tells nothing', async ($, on) => {
  let files = new Map<string, string>()
  const w = world(on, { tool: bottomTool, evaluate: questionGate(() => files, 'unmarked-question', 2) })
  files = w.files
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await $.tool.call(QUESTION as Any)
  await $.tool.call(QUESTION as Any)
  expect(w.toasts.some(t => t.includes('refused Claude twice'))).toBe(false)
  expect(stuckLines(w).length).toBe(0)
})

// VER-33: the compaction note is never doubled, names only a known step, and is left out once every step is reached.
test('VER-33: a compaction whose messages already hold an earlier note ends with exactly one note, the new one', async ($, on) => {
  const old = { role: 'user', text: NOTE('step 1 (Intake)'), toolUses: [] }
  const w = world(on, { tool: taskTools(), compact: () => ({ messages: [{ role: 'user', text: 'the summary', toolUses: [] }, old] }) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await markStep($, 2, 'Pick')
  await w.clock.advance(600)
  const out = (await ($ as Any).session.compact({ trigger: 'auto', messages: [...TALK, old] })) as Any
  expect(out.messages.map((m: Any) => m.text)).toEqual(['the summary', NOTE('step 2 (Pick)')])
})

test('VER-33: a marked step the last evaluation doesn\'t have counts as no step', async ($, on) => {
  const w = world(on, { tool: taskTools() })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await markStep($, 40, 'Ghost')
  await w.clock.advance(600)
  const out = (await ($ as Any).session.compact({ trigger: 'manual', messages: TALK })) as Any
  expect(w.compactIn[0].instructions).toBe("Keep, for DevForgeAI's progress tracker: in the architecture run, the task list marks no step in progress.")
  expect(out.messages.map((m: Any) => m.text)).toEqual(['the summary', NOTE('no step')])
})

test('VER-33: once every step is reached the instruction is kept but no note is added', async ($, on) => {
  const w = world(on, { tool: taskTools(), evaluate: () => ({ state: { ...STATE, current: null } }) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await markStep($, 2, 'Pick')
  await w.clock.advance(600)
  const out = (await ($ as Any).session.compact({ trigger: 'manual', messages: TALK })) as Any
  expect(w.compactIn[0].instructions).toBe("Keep, for DevForgeAI's progress tracker: in the architecture run, the task list marks step 2 (Pick) in progress.")
  expect(out.messages.map((m: Any) => m.text)).toEqual(['the summary'])
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes(' compact: ')).toBe(true)
})

// ---- version 8 review (2026-10-03): fixes and coverage the plugin-validator review asked for ----

test('VER-33 (review W1): the marked step is the evaluator\'s: a later unknown step\'s mark doesn\'t hide step 2', async ($, on) => {
  const w = world(on, { tool: taskTools() })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await markStep($, 2, 'Pick')
  await $.tool.call({ tool: 'TaskCreate', subject: '40. Ghost', description: 'd', metadata: { devforgeai_step: 40 } } as Any)
  await $.tool.call({ tool: 'TaskUpdate', taskId: '2', status: 'completed' } as Any)  // fails in taskTools only for in_progress
  await $.tool.call({ tool: 'TodoWrite', todos: [{ content: '40. Ghost', status: 'in_progress', activeForm: 'x' }] } as Any)
  expect(stepsOf(eventsOf(w, 'architecture')).slice(-1)).toEqual([[40, 'started']])
  await w.clock.advance(600)
  const out = (await ($ as Any).session.compact({ trigger: 'manual', messages: TALK })) as Any
  expect(out.messages.map((m: Any) => m.text)).toEqual(['the summary', NOTE('step 2 (Pick)')])
})

test('VER-30 (review N3): a tag naming a step the checklist lacks still gets the tag sentence', async ($, on) => {
  let files = new Map<string, string>()
  const w = world(on, { mode: 'enforce local', tool: bottomTool, evaluate: questionGate(() => files, 'unmarked-question', 2) })
  files = w.files
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  const r = (await $.tool.call(asked('devforgeai_step:40') as Any)) as Any
  expect(r.deny).toBe(QUESTION_REFUSAL + QUESTION_TAG)
})

for (const type of ['unmarked-question', 'mismatched-question']) {
  test(`VER-31 (review): a ${type} flag's toast also says its answer counted for no step`, async ($, on) => {
    const state = { ...STATE, flags: [{ gate: 'question', seq: 3, step: 2, type, message: MESSAGES[type] }] }
    const w = world(on, { tool: bottomTool, evaluate: () => ({ state }) })
    await start($)
    await load($, 'devforgeai:brainstorm', TAGGED)
    await w.clock.advance(600)
    expect(w.toasts.filter(t => t === `✗ Step 2 ${type}: ${MESSAGES[type]} Its answer, if any, counts for no step.`).length).toBe(1)
  })
}

test('VER-31 (review): the user\'s sentence is judged at the flag\'s seq: a prompt at or after it doesn\'t count', async ($, on) => {
  // markStep gives seqs 2 (TaskCreate), 3 (TaskUpdate) and 4 (step 2 started); the prompt is seq 5, the flag's seq.
  const state = { ...OBSERVED, gate: { kind: 'write', seq: 5, refuse: true, reason: 'x' },
    flags: [{ gate: 'write', seq: 5, step: 8, type: 'skipped', message: 'step 8 had no answer from you' }] }
  const w = world(on, { tool: taskTools(), evaluate: () => ({ state }) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await markStep($, 2, 'Pick')
  await typed($)
  expect(JSON.parse(eventsOf(w, 'architecture').slice(-1)[0])).toMatchObject({ seq: 5, kind: 'prompt' })
  await w.clock.advance(600)
  expect(w.toasts.filter(t => t === '✗ Step 8 skipped: step 8 had no answer from you').length).toBe(1)
})

test('VER-32 (review): the same flag type at another step is another cause', async ($, on) => {
  let files = new Map<string, string>()
  let step = 2
  const w = world(on, { mode: 'enforce local', tool: bottomTool, evaluate: argv => questionGate(() => files, 'unmarked-question', step)(argv) })
  files = w.files
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await $.tool.call(QUESTION as Any)
  step = 3
  await $.tool.call(QUESTION as Any)
  expect(w.toasts.some(t => t.includes('refused Claude twice'))).toBe(false)
  expect(stuckLines(w).length).toBe(0)
})

// ---- version 10: the waiver's recording (VER-34) and the end-of-run review (VER-35) ----

const WAIVER_SOURCE = 'devforgeai_waiver'

/** A waiver question: one question, tagged devforgeai_waiver, or another source / two questions when asked. */
function waiverQuestion(question: string, source: unknown = WAIVER_SOURCE, two = false): Any {
  const one = { question, header: 'Step 1', multiSelect: false,
    options: [{ label: 'Proceed without questions', description: 'd' }, { label: 'Ask me as usual', description: 'd' }] }
  return { tool: 'AskUserQuestion', questions: two ? [one, { ...one, question: 'And?' }] : [one], metadata: { source } }
}

/** The bottom of the world for questions: each question text's answer, a dismissal, or bottomTool. */
function answering(answers: Record<string, string | null>) {
  return (e: Any): Any => {
    if (e.tool === 'AskUserQuestion') {
      const q = e.questions?.[0]?.question ?? 'Q?'
      if (q in answers) {
        const a = answers[q]
        if (a === null) return { result: "Error: The user doesn't want to proceed with this tool use.", isError: true, text: "The user doesn't want to proceed with this tool use." }
        return { result: { questions: e.questions, answers: { [q]: a }, annotations: {} } }
      }
    }
    return taskTools()(e)
  }
}

test('VER-34: the waiver question\'s answer records which fixed label was picked, or other', async ($, on) => {
  const w = world(on, { tool: answering({ 'P?': 'Proceed without questions', 'A?': 'Ask me as usual', 'T?': 'not sure',
    'C?': 'proceed without questions', 'D?': null }) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  for (const q of ['P?', 'A?', 'T?', 'C?', 'D?']) await $.tool.call(waiverQuestion(q) as Any)
  const answers = eventsOf(w).map(l => JSON.parse(l)).filter(e => e.kind === 'answer')
  expect(answers.map(e => [e.answered, e.waiver ?? null, e.step ?? null, e.outside ?? null])).toEqual([
    [true, 'proceed', null, null], [true, 'ask', null, null], [true, 'other', null, null],
    [true, 'other', null, null], [false, 'other', null, null],
  ])
})

test('VER-34: two questions with the waiver source, or a near-miss source, are recorded as outside', async ($, on) => {
  const w = world(on, { tool: answering({ 'P?': 'Proceed without questions' }) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await $.tool.call(waiverQuestion('P?', WAIVER_SOURCE, true) as Any)
  await $.tool.call(waiverQuestion('P?', 'devforgeai_waiver ') as Any)
  await $.tool.call(waiverQuestion('P?', 'devforgeai_waivers') as Any)
  const answers = eventsOf(w).map(l => JSON.parse(l)).filter(e => e.kind === 'answer')
  expect(answers.map(e => [e.waiver ?? null, e.outside ?? null])).toEqual([[null, true], [null, true], [null, true]])
})

test('VER-34: in enforce mode the waiver question isn\'t checked: no pending file, and it goes on', async ($, on) => {
  const w = world(on, { mode: 'enforce local', evaluate: questioningAt(2),
    tool: answering({ 'P?': 'Proceed without questions' }) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  const r = (await $.tool.call(waiverQuestion('P?') as Any)) as Any
  expect(r.deny).toBeUndefined()
  expect(w.writes.filter(p => p.endsWith('/pending.jsonl')).length).toBe(0)
  expect(eventsOf(w).map(l => JSON.parse(l)).filter(e => e.kind === 'answer').map(e => e.waiver)).toEqual(['proceed'])
})

test('VER-34: a mod\'s own question isn\'t recorded', { plugins: [asker] }, async ($, on) => {
  const w = world(on, { tool: bottomTool })
  await start($)
  await load($)
  await ($ as Any).command.run({ command: 'ask-me', args: '' })
  expect(kinds(eventsOf(w))).toEqual(['skill-loaded'])
})

/** Every step reached and the run open: the state BEH-26 reviews, with these flags. */
function reached(flags: Any[]) {
  return { ...STATE, current: null, flags }
}

const FLAG8 = { gate: 'write', seq: 30, step: 8, type: 'skipped',
  message: 'step 8 (Propose and confirm the outcome) had no answer from you before docs/specs/arch/ARCH-001.md was written' }

/** Review answers in order (null: dismissed), recording each review question asked. */
function reviewing(script: Array<string | null>, asked: string[]) {
  const answers = [...script]
  return (e: Any): Any => {
    if (e.tool === 'AskUserQuestion' && e.questions?.[0]?.header === 'Review') {
      const q = e.questions[0].question
      asked.push(q)
      const a = answers.shift()
      if (a === null || a === undefined) return { result: "Error: The user doesn't want to proceed with this tool use.", isError: true, text: "The user doesn't want to proceed with this tool use." }
      return { result: { questions: e.questions, answers: { [q]: a }, annotations: {} } }
    }
    return taskTools()(e)
  }
}

/** A world whose question checks refuse an unmarked question at step 2, and whose other evaluations show `state`. */
function reviewWorld(on: Any, state: () => Any, script: Array<string | null>, asked: string[], over: Over = {}) {
  let files = new Map<string, string>()
  const gate = questionGate(() => files, 'unmarked-question', 2)
  const w = world(on, { mode: 'enforce local', tool: reviewing(script, asked),
    evaluate: argv => (argv[argv.indexOf('--events') + 1].endsWith('/pending.jsonl') ? gate(argv) : { state: state() }), ...over })
  files = w.files
  return w
}

async function turnEnd($: Any) {
  await $.turn.complete({ turnId: 't', answer: '', durationMs: 1, isAborted: false, reason: 'answer', usage: null })
}

function reviewLines(w: World): Any[] {
  const path = runFiles(w, 'review.jsonl').slice(-1)[0]
  return path ? (w.files.get(path) ?? '').split('\n').filter(Boolean).map(l => JSON.parse(l)) : []
}

test('VER-35: a finished run with a refusal and a flag is reviewed, refusals first, answers recorded', async ($, on) => {
  const asked: string[] = []
  let state: Any = STATE
  const w = reviewWorld(on, () => state, ['Accept', 'I did answer it'], asked)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await $.tool.call(QUESTION as Any)  // refused: unmarked-question at step 2
  state = reached([FLAG8])
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked).toEqual([
    'architecture run, item 1 of 2: refused 1 time(s) at step 2 (question gate): ' + MESSAGES['unmarked-question'] + '. Accept it, or challenge it?',
    'architecture run, item 2 of 2: flagged at step 8 (write gate): ' + FLAG8.message + '. Accept it, or challenge it?',
  ])
  expect(reviewLines(w).map(l => [l.item, l.gate, l.step, l.type, l.refused, l.answer, l.reason])).toEqual([
    [1, 'question', 2, 'unmarked-question', 1, 'accept', null],
    [2, 'write', 8, 'skipped', 0, 'challenge', 'I did answer it'],
  ])
  const log = (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' review: '))
  expect(log.length).toBe(2)
  expect(w.toasts.some(t => t.startsWith('architecture: your review is in devforgeai/progress/runs/'))).toBe(true)
  // Nothing reaches Claude: the next prompt carries no added context.
  await $.prompt.submit({ text: 'thanks', origin: { kind: 'composer' } } as Any)
  expect(w.contexts.slice(-1)[0]).toEqual([])
  // A later turn end asks nothing again.
  await turnEnd($)
  expect(asked.length).toBe(2)
})

test('VER-35: Challenge records no reason; three refusals and a flag for one cause are one item', async ($, on) => {
  const asked: string[] = []
  let state: Any = STATE
  const w = reviewWorld(on, () => state, ['Challenge'], asked)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  for (let i = 0; i < 3; i++) await $.tool.call(QUESTION as Any)
  state = reached([{ gate: 'question', seq: 40, step: 2, type: 'unmarked-question', message: MESSAGES['unmarked-question'] }])
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked.length).toBe(1)
  expect(asked[0].startsWith('architecture run, item 1 of 1: refused 3 time(s) at step 2 (question gate): ')).toBe(true)
  expect(reviewLines(w).map(l => [l.refused, l.answer, l.reason])).toEqual([[3, 'challenge', null]])
})

test('VER-35: a dismissal records that item and the rest as dismissed and asks nothing more', async ($, on) => {
  const asked: string[] = []
  let state: Any = STATE
  const w = reviewWorld(on, () => state, [null], asked)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await $.tool.call(QUESTION as Any)
  state = reached([FLAG8, { ...FLAG8, step: 9, type: 'rule-broken', message: 'docs/specs/arch/ARCH-001.md sets outcome: create, which needs your answer at step 8' }])
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked.length).toBe(1)
  expect(reviewLines(w).map(l => [l.item, l.answer])).toEqual([[1, 'dismissed'], [2, 'dismissed'], [3, 'dismissed']])
})

test('VER-35: no review for a run not every step of which is reached, nor for a run with no item', async ($, on) => {
  const asked: string[] = []
  let state: Any = { ...STATE, flags: [FLAG8] }
  const w = reviewWorld(on, () => state, ['Accept'], asked)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked.length).toBe(0)
  state = reached([])
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked.length).toBe(0)
  expect(runFiles(w, 'review.jsonl').length).toBe(0)
})

test('VER-35: observe mode reviews a flagged run too; a session where nothing draws doesn\'t', async ($, on) => {
  const asked: string[] = []
  const w = reviewWorld(on, () => reached([FLAG8]), ['Accept'], asked, { mode: 'observe framework-default' })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked.length).toBe(1)
})

test('VER-35: a session where nothing draws gets no review', async ($, on) => {
  const asked: string[] = []
  const w = reviewWorld(on, () => reached([FLAG8]), ['Accept'], asked, { surfaces: [] })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked.length).toBe(0)
})

test('VER-35 (review M1): a last step reached in the turn\'s final events is evaluated before the review decides', async ($, on) => {
  const asked: string[] = []
  let state: Any = STATE
  const w = reviewWorld(on, () => state, ['Accept'], asked)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)  // evaluated: step 2 current, nothing to review yet
  state = reached([FLAG8])     // the next evaluation will show every step reached
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/docs/specs/arch/ARCH-001.md` } as Any)  // unevaluated
  await turnEnd($)             // no timer tick in between
  expect(asked.length).toBe(1)
})

// ---- version 11: the review only after an answered turn (VER-37) ----

async function turnEndFor($: Any, reason: string | undefined) {
  const e: Any = { turnId: 't', answer: '', durationMs: 1, isAborted: reason === 'aborted', usage: null }
  if (reason !== undefined) e.reason = reason
  if (reason === 'refusal') e.refusal = { category: null, explanation: 'refused' }
  await $.turn.complete(e)
}

test('VER-37: a turn ended by Esc, a refusal, an error or with no reason asks nothing; the next answered turn asks', async ($, on) => {
  const asked: string[] = []
  let state: Any = STATE
  const w = reviewWorld(on, () => state, ['Accept'], asked)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await $.tool.call(QUESTION as Any)  // refused: unmarked-question at step 2
  state = reached([])
  await w.clock.advance(600)
  for (const reason of ['aborted', 'refusal', 'error', undefined]) {
    await turnEndFor($, reason)
    expect(asked.length).toBe(0)
    expect(runFiles(w, 'review.jsonl').length).toBe(0)
  }
  await turnEndFor($, 'answer')
  expect(asked).toEqual([
    'architecture run, item 1 of 1: refused 1 time(s) at step 2 (question gate): ' + MESSAGES['unmarked-question'] + '. Accept it, or challenge it?',
  ])
  expect(reviewLines(w).map(l => l.answer)).toEqual(['accept'])
})

// ---- version 12: the deliberate stop (VER-38) and the exit confirmation (VER-39) ----

/** An architecture state at step `current` of 11; step 8 is stoppable, as the manifest says (SPEC-012 v12). */
function archState(current: number | null, ended: string | null = null, flags: Any[] = []): Any {
  const steps = Array.from({ length: 11 }, (_, i) => {
    const n = i + 1
    const state = current === null ? 'done' : n < current ? 'done' : n === current ? 'current' : 'pending'
    return { n, title: `Step ${n}`, state, userOwned: n === 7 || n === 8, ...(n === 8 ? { stoppable: true } : {}) }
  })
  return { ...STATE, skill: 'architecture', current, ended, steps, flags }
}

/** An AskUserQuestion of one question tagged with a step (or another source, or two questions). */
function stepQuestion(question: string, source: unknown = 'devforgeai_step:8', two = false): Any {
  const one = { question, header: 'Step 8', multiSelect: false,
    options: [{ label: 'Confirm amend', description: 'd' }, { label: 'Write nothing', description: 'd' }] }
  return { tool: 'AskUserQuestion', questions: two ? [one, { ...one, question: 'And?' }] : [one], metadata: { source } }
}

function runEnds(w: World): string[] {
  return eventsOf(w, 'architecture').map(l => JSON.parse(l)).filter(e => e.kind === 'run-end').map(e => e.reason)
}

test('VER-38: Write nothing on a stoppable step ends the run as stopped, shown and reviewed at the stop', async ($, on) => {
  const asked: string[] = []
  let state: Any = archState(8, null, [FLAG8])
  const w = world(on, { evaluate: () => ({ state }),
    tool: (e: Any) => e.questions?.[0]?.header === 'Review' ? reviewing(['Accept'], asked)(e) : answering({ 'Outcome?': 'Write nothing' })(e) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  state = archState(8, 'stopped', [FLAG8])  // what the evaluator gives once the run-end is logged
  await $.tool.call(stepQuestion('Outcome?') as Any)
  const events = eventsOf(w, 'architecture').map(l => JSON.parse(l))
  expect(events.slice(-2).map(e => [e.kind, e.step ?? e.reason])).toEqual([['answer', 8], ['run-end', 'stopped']])
  await w.clock.advance(600)  // the run-end marked the run; the timer evaluates it (no tool call waits, BEH-06)
  expect(w.statuses[w.statuses.length - 1]).toBe('architecture stopped at step 8 · 1 flag')
  await turnEnd($)
  expect(asked.length).toBe(1)
  expect(asked[0]).toContain('architecture run, item 1 of 1: flagged at step 8')
  await load($, 'devforgeai:architecture', TAGGED)  // a new run: the stopped run gets no another-skill run-end
  expect(runsOf(w, 'architecture')[0].map(l => JSON.parse(l)).filter(e => e.kind === 'run-end').map(e => e.reason))
    .toEqual(['stopped'])
})

test('VER-38: with no state.json yet, nothing stops', async ($, on) => {
  const w = world(on, { evaluate: () => ({ state: archState(8) }), tool: answering({ 'Outcome?': 'Write nothing' }) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await $.tool.call(stepQuestion('Outcome?') as Any)  // no evaluation has run, so no state says step 8 is stoppable
  expect(runEnds(w)).toEqual([])
})

test('VER-38: a session end after the stop writes no second run-end', async ($, on) => {
  const w = world(on, { evaluate: () => ({ state: archState(8) }), tool: answering({ 'Outcome?': 'Write nothing' }) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await $.tool.call(stepQuestion('Outcome?') as Any)
  await $.session.end({ reason: 'clear', sessionId: 's1', resume: { id: 's1' } } as Any)
  expect(runEnds(w)).toEqual(['stopped'])
})

test('VER-38: no stop on a step that isn\'t stoppable, two questions, an untagged or waiver question, or a dismissal', async ($, on) => {
  const w = world(on, { evaluate: () => ({ state: archState(8) }),
    tool: answering({ 'Outcome?': 'Write nothing', 'Gone?': null }) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await $.tool.call(stepQuestion('Outcome?', 'devforgeai_step:7') as Any)
  await $.tool.call(stepQuestion('Outcome?', 'devforgeai_step:8', true) as Any)
  await $.tool.call(stepQuestion('Outcome?', null) as Any)
  await $.tool.call(stepQuestion('Outcome?', 'devforgeai_waiver') as Any)
  await $.tool.call(stepQuestion('Gone?') as Any)
  expect(runEnds(w)).toEqual([])
})

/** command.run's bottom: the built-in command running; records which ran. */
function commands(on: Any, ran: string[]) {
  on('command.run', (_$: Any, e: Any) => { ran.push(e.command); return { text: `ran ${e.command}` } })
}

/** Answers the exit dialog (header Progress): a label, a dismissal (null), or a failure ('FAIL'). */
function confirming(answer: string | null, asked: string[]) {
  return (e: Any): Any => {
    if (e.tool === 'AskUserQuestion' && e.questions?.[0]?.header === 'Progress') {
      const q = e.questions[0].question
      asked.push(q)
      if (answer === 'FAIL') return { deny: 'the dialog could not be shown' }
      if (answer === null) return { result: "Error: The user doesn't want to proceed with this tool use.", isError: true, text: "The user doesn't want to proceed with this tool use." }
      return { result: { questions: e.questions, answers: { [q]: answer }, annotations: {} } }
    }
    return taskTools()(e)
  }
}

const COMPOSER = { kind: 'composer' }

test('VER-39: /clear while a run is unfinished asks; Keep working keeps it, Clear anyway runs it', async ($, on) => {
  const asked: string[] = []
  const ran: string[] = []
  let answer: string | null = 'Keep working'
  const w = world(on, { evaluate: () => ({ state: archState(3) }), tool: (e: Any) => confirming(answer, asked)(e) })
  commands(on, ran)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  const kept = (await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)) as Any
  expect(asked).toEqual(['architecture run is at step 3 of 11 and unfinished. Clear anyway?'])
  expect(kept.text).toBe('Kept working: the architecture run is still at step 3.')
  expect(ran).toEqual([])
  expect((w.files.get(`${SESSION}/adapter.log`) ?? '').includes(' exit: ')).toBe(true)
  answer = 'Clear anyway'
  await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)
  expect(ran).toEqual(['clear'])
})

test('VER-39: exit and resume ask with their verbs; a dismissal keeps; a failed dialog runs the command', async ($, on) => {
  const asked: string[] = []
  const ran: string[] = []
  let answer: string | null = null
  const w = world(on, { evaluate: () => ({ state: archState(3) }), tool: (e: Any) => confirming(answer, asked)(e) })
  commands(on, ran)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await $.command.run({ command: 'exit', args: '', origin: COMPOSER } as Any)
  expect(ran).toEqual([])
  answer = 'FAIL'
  await $.command.run({ command: 'resume', args: '', origin: COMPOSER } as Any)
  expect(ran).toEqual(['resume'])
  expect(asked).toEqual(['architecture run is at step 3 of 11 and unfinished. Exit anyway?',
    'architecture run is at step 3 of 11 and unfinished. Resume anyway?'])
  const log = (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' exit: '))
  expect(log.length).toBe(2)
  expect(log[0]).toContain('kept /exit: dismissed')
  expect(log[1]).toContain('ran /resume: the dialog failed')
})

test('VER-39: the dialog offers <Verb> anyway and Keep working under Progress; bridge asks; nothing draws runs', async ($, on) => {
  const seen: Any[] = []
  const ran: string[] = []
  const over: Any = { surfaces: ['terminal'], evaluate: () => ({ state: archState(3) }), tool: (e: Any) => {
    if (e.tool === 'AskUserQuestion' && e.questions?.[0]?.header === 'Progress') seen.push(e.questions[0])
    return confirming('Keep working', [])(e)
  } }
  const w = world(on, over)
  commands(on, ran)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await $.command.run({ command: 'clear', args: '', origin: { kind: 'bridge' } } as Any)
  expect(seen.map(q => [q.header, q.options.map((o: Any) => o.label)])).toEqual([['Progress', ['Clear anyway', 'Keep working']]])
  over.surfaces = []
  await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)
  expect(seen.length).toBe(1)
  expect(ran).toEqual(['clear'])
})

test('VER-39: nothing is asked for other commands, a plugin\'s run, a finished or stopped run, no state, no run, headless', async ($, on) => {
  const asked: string[] = []
  const ran: string[] = []
  let state: Any = archState(3)
  const w = world(on, { evaluate: () => ({ state }), tool: (e: Any) => confirming('Keep working', asked)(e) })
  commands(on, ran)
  await start($)
  await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)   // no open run
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await $.command.run({ command: 'compact', args: '', origin: COMPOSER } as Any)
  await $.command.run({ command: 'branch', args: '', origin: COMPOSER } as Any)
  await $.command.run({ command: 'clear', args: '', origin: { kind: 'plugin', name: 'other' } } as Any)
  state = archState(null)
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/docs/x.md` } as Any)  // an event, so the run is evaluated again
  await w.clock.advance(600)
  await $.command.run({ command: 'exit', args: '', origin: COMPOSER } as Any)    // every step reached
  state = archState(8, 'stopped')
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/docs/y.md` } as Any)
  await w.clock.advance(600)
  await $.command.run({ command: 'exit', args: '', origin: COMPOSER } as Any)    // stopped
  expect(asked).toEqual([])
  expect(ran).toEqual(['clear', 'compact', 'branch', 'clear', 'exit', 'exit'])
})

test('VER-39: with no state (the evaluator can\'t run), nothing is asked', async ($, on) => {
  const asked: string[] = []
  const ran: string[] = []
  const w = world(on, { python3: false, tool: (e: Any) => confirming('Keep working', asked)(e) })
  commands(on, ran)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)
  expect(asked).toEqual([])
  expect(ran).toEqual(['clear'])
})

test('VER-39: a headless session asks nothing', async ($, on) => {
  const asked: string[] = []
  const ran: string[] = []
  const w = world(on, { evaluate: () => ({ state: archState(3) }), tool: (e: Any) => confirming('Keep working', asked)(e) })
  commands(on, ran)
  await start($, false)
  await load($, 'devforgeai:architecture', TAGGED)
  await w.clock.advance(600)
  await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)
  expect(asked).toEqual([])
  expect(ran).toEqual(['clear'])
})

// ---- version 14: pausing and resuming nested runs (VER-43, VER-44); version 13's VER-41 tests are replaced ----
/** Tasks for steps 1..n (IDs from `first`), then step `marked` in progress. */
async function taskList($: Any, n: number, marked: number, first = 1) {
  for (let i = 1; i <= n; i++) await $.tool.call({ tool: 'TaskCreate', subject: `${i}. Step ${i}`, description: 'd', metadata: { devforgeai_step: i } } as Any)
  await $.tool.call({ tool: 'TaskUpdate', taskId: String(first + marked - 1), status: 'in_progress' } as Any)
}

/** A Skill tool call held open while skill.prompt fires inside it, as Claude Code does (probe, 2026-10-05). */
function skillCalls(): { tool: (e: Any) => Any; load: ($: Any, skill: string, agentId?: string, during?: () => Promise<void>) => Promise<string> } {
  let release: (() => void) | null = null
  const inner = taskTools()
  return {
    tool: (e: Any): Any => {
      if (e.tool === 'Skill') return new Promise(resolve => { release = () => resolve({ result: 'Launching skill' }) })
      return inner(e)
    },
    async load($: Any, skill: string, agentId?: string, during?: () => Promise<void>): Promise<string> {
      const call = $.tool.call({ tool: 'Skill', skill, ...(agentId ? { agentId } : {}) } as Any)
      for (let i = 0; i < 50; i++) await Promise.resolve()  // the call reaches the stub, holding the skill in flight
      if (during) await during()
      const out = (await $.skill.prompt({ skill, text: TAGGED })) as Any
      release?.()
      await call
      return out.text
    },
  }
}

function trailLog(w: World): string[] {
  return (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' trail: ')).map(l => l.split(' trail: ')[1])
}
const RETURN_7 = "This skill was loaded by architecture at step 7. When this skill's work is done, mark architecture's step 7 task in progress again and continue architecture at step 7."

/** A state of `skill` with `n` steps, `current` current (null: every step reached), `ended`, `flags`. */
function skState(skill: string, n: number, current: number | null, ended: string | null = null, flags: Any[] = [], stoppable: number[] = []): Any {
  const steps = Array.from({ length: n }, (_, i) => {
    const k = i + 1
    const state = current === null ? 'done' : k < current ? 'done' : k === current ? 'current' : 'pending'
    return { n: k, title: `Step ${k}`, state, userOwned: stoppable.includes(k), ...(stoppable.includes(k) ? { stoppable: true } : {}) }
  })
  return { ...STATE, skill, current, ended, steps, flags }
}

/** A world for nested runs: Skill calls held open as Claude Code does, the evaluator answering by the run's skill. */
function nestWorld(on: Any, states: Record<string, () => Any>, over: Over = {}, ask?: (e: Any) => Any) {
  const sk = skillCalls()
  const w = world(on, { tool: (e: Any) => (ask && e.tool === 'AskUserQuestion' ? ask(e) : sk.tool(e)), ...over, evaluate: argv => {
    const ev = argv[argv.indexOf('--events') + 1]
    const skill = Object.keys(states).find(s => ev.includes(`-${s}-`))
    return { state: skill ? states[skill]() : STATE }
  } })
  return { w, sk }
}

function logOf(w: World, skill: string, i = 0): Any[] {
  return (runsOf(w, skill)[i] ?? []).map(l => JSON.parse(l))
}

const READ = (name: string) => ({ tool: 'Read', file_path: `${ROOT}/docs/${name}` } as Any)

test('VER-43: a skill Claude loads mid-run pauses the open run and gets the return line', async ($, on) => {
  const { w, sk } = nestWorld(on, { architecture: () => archState(7), 'spec-lookup': () => skState('spec-lookup', 5, 2) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await w.clock.advance(600)
  const text = await sk.load($, 'devforgeai:spec-lookup')
  expect(text.endsWith('\n\n' + RETURN_7)).toBe(true)
  expect(logOf(w, 'architecture').some(e => e.kind === 'run-end')).toBe(false)
  expect(logOf(w, 'spec-lookup')[0].checklist.includes('This skill was loaded by')).toBe(false)
  expect(trailLog(w)).toEqual(['push architecture at step 7 (1 on the trail)'])
  await $.tool.call(READ('a.md'))
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('spec-lookup 2/5 · in architecture 7/11')
  expect(logOf(w, 'spec-lookup').slice(-1)[0]).toMatchObject({ kind: 'tool', tool: 'Read' })
  expect(logOf(w, 'architecture').some(e => e.path === 'docs/a.md')).toBe(false)
})

test('VER-43: three nested loads give a trail of three; a load of the open run\'s own skill changes nothing', async ($, on) => {
  const { w, sk } = nestWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), architecture: () => archState(7),
    prd: () => skState('prd', 9, 3), 'spec-lookup': () => skState('spec-lookup', 5, 2) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await w.clock.advance(600)
  await sk.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await w.clock.advance(600)
  await sk.load($, 'devforgeai:prd')
  await taskList($, 9, 3, 20)
  await w.clock.advance(600)
  await sk.load($, 'devforgeai:spec-lookup')
  await taskList($, 5, 2, 29)
  expect(await sk.load($, 'devforgeai:spec-lookup')).not.toContain('This skill was loaded by')
  expect(runsOf(w, 'spec-lookup').length).toBe(1)
  await $.tool.call(READ('b.md'))
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('spec-lookup 2/5 · in prd 3/9 · 2 more')
})

test('VER-43: a typed load ends the open run and every paused run; a Claude load that can\'t nest ends the open run only', async ($, on) => {
  const { w, sk } = nestWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), architecture: () => archState(1),
    'spec-lookup': () => skState('spec-lookup', 5, 2), prd: () => skState('prd', 9, 1) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await sk.load($, 'devforgeai:architecture')        // brainstorm paused
  await sk.load($, 'devforgeai:spec-lookup')         // architecture keeps no task list: it ends, brainstorm stays paused
  expect(logOf(w, 'architecture').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  expect(logOf(w, 'brainstorm').some(e => e.kind === 'run-end')).toBe(false)
  await $.skill.prompt({ skill: 'devforgeai:prd', text: TAGGED })   // typed: no Skill call in flight
  expect(logOf(w, 'spec-lookup').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  expect(logOf(w, 'brainstorm').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  expect(trailLog(w).slice(-1)[0]).toBe('empty (a load with no Skill call of the main loop in flight)')
})

test('VER-43: a subagent\'s Skill call and an untracked skill pause nothing; a load that isn\'t Claude\'s ends the open run and the paused ones', async ($, on) => {
  const { w, sk } = nestWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), architecture: () => archState(7) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await sk.load($, 'devforgeai:architecture')                      // brainstorm paused
  await taskList($, 11, 7, 9)
  expect(await sk.load($, 'other:helper')).not.toContain('This skill was loaded by')
  expect(trailLog(w)).toEqual(['push brainstorm at step 4 (1 on the trail)'])
  expect(logOf(w, 'brainstorm').some(e => e.kind === 'run-end')).toBe(false)   // paused, not ended
  expect(logOf(w, 'architecture').some(e => e.kind === 'run-end')).toBe(false)
  expect(await sk.load($, 'devforgeai:spec-lookup', 'agent-1')).not.toContain('This skill was loaded by')
  expect(logOf(w, 'architecture').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  expect(logOf(w, 'brainstorm').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
})

test('VER-43: a TaskCreate under way when the load comes records its task in the run open when it lands', async ($, on) => {
  let hold: (() => void) | null = null
  const inner = skillCalls()
  const w = world(on, { tool: (e: Any) => {
    if (e.tool === 'TaskCreate' && e.subject === '9. Late') return new Promise(r => { hold = () => r({ result: { task: { id: '50' } }, text: 'Task #50 created successfully: 9. Late' }) })
    return inner.tool(e)
  } })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  const late = $.tool.call({ tool: 'TaskCreate', subject: '9. Late', description: 'd', metadata: { devforgeai_step: 9 } } as Any)
  for (let i = 0; i < 50; i++) await Promise.resolve()
  const loading = inner.load($, 'devforgeai:spec-lookup')   // may wait up to 2 s for the TaskCreate under way (BEH-29)
  for (let i = 0; i < 50; i++) await Promise.resolve()
  await w.clock.advance(2500)
  hold?.()
  await loading
  await late
  expect(logOf(w, 'architecture').some(e => e.kind === 'run-end')).toBe(false)               // paused
  expect(logOf(w, 'architecture').some(e => e.tool === 'TaskCreate' && e.seq > 13)).toBe(false)
  expect(logOf(w, 'spec-lookup').some(e => e.tool === 'TaskCreate')).toBe(true)              // recorded where it landed
})

test('VER-43: a compaction while nested ends with the return note once; a second session.start keeps the trail', async ($, on) => {
  const { w, sk } = nestWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), architecture: () => archState(7) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await sk.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await sk.load($, 'devforgeai:spec-lookup')
  expect(logOf(w, 'brainstorm').some(e => e.kind === 'run-end') || logOf(w, 'architecture').some(e => e.kind === 'run-end')).toBe(false)
  const note = 'Return points (from the progress tracker): when spec-lookup is done, continue architecture at step 7; then brainstorm at step 4.'
  const out = (await ($ as Any).session.compact({ trigger: 'manual', instructions: '', messages: TALK })) as Any
  expect(out.messages.filter((m: Any) => m.text === note).length).toBe(1)
  await start($)
  const again = (await ($ as Any).session.compact({ trigger: 'manual', instructions: '', messages: [...out.messages] })) as Any
  expect(again.messages.filter((m: Any) => m.text === note).length).toBe(1)
  expect(w.compactIn.length).toBe(2)
})

test('VER-44: once the nested run has worked, re-marking the outer\'s step resumes it; before, nothing', async ($, on) => {
  const { w, sk } = nestWorld(on, { architecture: () => archState(7), 'spec-lookup': () => skState('spec-lookup', 5, 2) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await w.clock.advance(600)
  await sk.load($, 'devforgeai:spec-lookup')
  await $.tool.call({ tool: 'TaskUpdate', taskId: '7', status: 'in_progress' } as Any)   // nothing done yet
  expect(trailLog(w)).toEqual(['push architecture at step 7 (1 on the trail)'])
  await $.tool.call({ tool: 'TaskUpdate', taskId: '2', status: 'in_progress' } as Any)      // fails (taskTools), a task of the paused run
  await $.tool.call({ tool: 'TodoWrite', todos: [] } as Any)                             // never resumes
  await $.tool.call(READ('c.md'))
  await $.tool.call({ tool: 'TaskUpdate', taskId: '7', status: 'in_progress' } as Any)
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
  expect(logOf(w, 'spec-lookup').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'returned' })
  const arch = logOf(w, 'architecture')
  expect(arch.slice(-2).map(e => [e.kind, e.tool ?? e.step])).toEqual([['tool', 'TaskUpdate'], ['step', 7]])
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('architecture 7/11')
})

test('VER-44: a stop of the nested run resumes the run beneath and keeps it in returned as stopped', async ($, on) => {
  const { w, sk } = nestWorld(on, { brainstorm: () => skState('brainstorm', 8, 4),
    architecture: () => skState('architecture', 11, 8, null, [], [8]) }, {}, answering({ 'Outcome?': 'Write nothing' }))
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await sk.load($, 'devforgeai:architecture')
  await w.clock.advance(600)
  const ask = { tool: 'AskUserQuestion', questions: [{ question: 'Outcome?', header: 'Step 8', multiSelect: false,
    options: [{ label: 'Confirm amend', description: 'd' }, { label: 'Write nothing', description: 'd' }] }], metadata: { source: 'devforgeai_step:8' } }
  await $.tool.call(ask as Any)
  expect(logOf(w, 'architecture').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'stopped' })
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to brainstorm at step 4 (1 returned, 0 on the trail)')
})

test('VER-44: at an answered turn\'s end, a nested run with every step reached resumes the run beneath; aborted, nothing', async ($, on) => {
  let inner: Any = skState('spec-lookup', 5, 3)
  const { w, sk } = nestWorld(on, { architecture: () => archState(7), 'spec-lookup': () => inner })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await sk.load($, 'devforgeai:spec-lookup')
  inner = skState('spec-lookup', 5, null)
  await $.tool.call(READ('d.md'))
  await turnEndFor($, 'aborted')
  expect(trailLog(w).length).toBe(1)
  await turnEndFor($, 'answer')
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
  expect(logOf(w, 'architecture').slice(-1)[0]).toMatchObject({ kind: 'turn', phase: 'end' })
})

test('VER-44: a Claude load of a paused run\'s skill unwinds to it and opens no run', async ($, on) => {
  const { w, sk } = nestWorld(on, { architecture: () => archState(7), 'spec-lookup': () => skState('spec-lookup', 5, 2) })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await sk.load($, 'devforgeai:spec-lookup')
  expect(await sk.load($, 'devforgeai:architecture')).not.toContain('This skill was loaded by')
  expect(runsOf(w, 'architecture').length).toBe(1)
  expect(logOf(w, 'spec-lookup').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'returned' })
  await $.tool.call(READ('e.md'))
  expect(logOf(w, 'architecture').slice(-1)[0]).toMatchObject({ kind: 'tool', path: 'docs/e.md' })
})

test('VER-44: a returned run with an item is reviewed at the turn\'s end, before the open run\'s own review', async ($, on) => {
  const asked: string[] = []
  const { w, sk } = nestWorld(on, { architecture: () => archState(7), 'spec-lookup': () => skState('spec-lookup', 5, 2, null, [FLAG8]) },
    {}, reviewing(['Accept'], asked))
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await sk.load($, 'devforgeai:spec-lookup')
  await $.tool.call(READ('f.md'))
  await w.clock.advance(600)                                        // spec-lookup's state, with its flag, is evaluated
  await $.tool.call({ tool: 'TaskUpdate', taskId: '7', status: 'in_progress' } as Any)
  await turnEndFor($, 'answer')
  expect(asked.length).toBe(1)
  expect(asked[0].startsWith('spec-lookup run, item 1 of 1: flagged at step 8')).toBe(true)
  expect(runFiles(w, 'review.jsonl').some(p => p.includes('-spec-lookup-'))).toBe(true)
})

test('VER-44: session.end with a trail of two ends every run, bottom first', async ($, on) => {
  const { w, sk } = nestWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), architecture: () => archState(7) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await sk.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await sk.load($, 'devforgeai:spec-lookup')
  await $.session.end({ reason: 'resume', sessionId: 's1', resume: { id: 's1' } } as Any)
  for (const s of ['brainstorm', 'architecture', 'spec-lookup']) {
    expect(logOf(w, s).slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'session-end' })
  }
  const times = ['brainstorm', 'architecture', 'spec-lookup'].map(s => w.writes.lastIndexOf(runFiles(w, 'events.jsonl').find(p => p.includes(`-${s}-`))!))
  expect(times[0] < times[1] && times[1] < times[2]).toBe(true)
})

test('VER-44: /clear while nested asks the nested question and keeps with the nested text', async ($, on) => {
  const asked: string[] = []
  const ran: string[] = []
  const { w, sk } = nestWorld(on, { architecture: () => archState(7), 'spec-lookup': () => skState('spec-lookup', 5, 2) },
    {}, confirming('Keep working', asked))
  commands(on, ran)
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await sk.load($, 'devforgeai:spec-lookup')
  await $.tool.call(READ('g.md'))
  await w.clock.advance(600)
  const kept = (await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)) as Any
  expect(asked).toEqual(['spec-lookup run is at step 2 of 5 and unfinished (architecture paused at step 7). Clear anyway?'])
  expect(kept.text).toBe('Kept working: the spec-lookup run goes on, and architecture is still paused at step 7.')
  expect(ran).toEqual([])
})

// ---- version 14, the clauses of VER-43 and VER-44 the first thirteen tests leave out ----
// Helpers are prefixed x so they can't clash with the drafted file's.

const xSettle = async () => { for (let i = 0; i < 50; i++) await Promise.resolve() }

const xReturn = (skill: string, step: number) =>
  `This skill was loaded by ${skill} at step ${step}. When this skill's work is done, mark ${skill}'s step ${step} task in progress again and continue ${skill} at step ${step}.`

const XSFLAG = { gate: 'write', seq: 40, step: 3, type: 'skipped', message: 'step 3 (Step 3) had no answer from you before docs/x.md was written' }
const XAFLAG = { gate: 'write', seq: 41, step: 7, type: 'skipped', message: 'step 7 (Step 7) had no answer from you before docs/y.md was written' }
const XBFLAG = { gate: 'report', seq: 42, step: 2, type: 'skipped', message: 'step 2 (Step 2) had no answer from you' }

/**
 * A world for nested runs whose tool calls can be held open: the Skill call (as skillCalls() does), a Read of
 * slow.md, the TaskCreate '3. Late' and a TaskUpdate whose activeForm starts with HOLD (the key). `route` answers a
 * call first (undefined passes it on), so tool answers go through world()'s over.tool and never a second
 * on('tool.call'). `load` runs the Skill call and the skill.prompt inside it; `before` runs after the call is
 * dispatched and before skill.prompt fires, `mid` while skill.prompt is pending.
 */
function xWorld(on: Any, states: Record<string, () => Any>, route?: (e: Any) => Any, over: Over = {}) {
  const sk = skillCalls()
  const held = new Map<string, () => void>()
  let releaseSkill: (() => void) | null = null
  const tool = (e: Any): Any => {
    if (e.tool === 'Skill') return new Promise(r => { releaseSkill = () => r({ result: 'Launching skill' }) })
    const key = e.tool === 'Read' && String(e.file_path).endsWith('/slow.md') ? 'read'
      : e.tool === 'TaskCreate' && e.subject === '3. Late' ? 'create'
      : e.tool === 'TaskUpdate' && String(e.activeForm ?? '').startsWith('HOLD') ? String(e.activeForm) : null
    if (key !== null) {
      return new Promise(r => held.set(key, () => r(key === 'create'
        ? { result: { task: { id: '50' } }, text: 'Task #50 created successfully: 3. Late' } : { result: 'ok' })))
    }
    return route?.(e) ?? sk.tool(e)
  }
  const w = world(on, Object.assign(over, { tool, evaluate: (argv: readonly string[]) => {
    const ev = argv[argv.indexOf('--events') + 1]
    const skill = Object.keys(states).find(s => ev.includes(`-${s}-`))
    return { state: skill ? states[skill]() : STATE }
  } }))
  return {
    w,
    release: (key: string) => held.get(key)?.(),
    async load($: Any, skill: string, hooks: { before?: () => Promise<void>; mid?: () => Promise<void> } = {}): Promise<string> {
      const call = $.tool.call({ tool: 'Skill', skill } as Any)
      await xSettle()
      await hooks.before?.()
      const prompt = $.skill.prompt({ skill, text: TAGGED }) as Promise<Any>
      await xSettle()
      await hooks.mid?.()
      const out = await prompt
      releaseSkill?.()
      await call
      return out.text
    },
  }
}

type XWorld = ReturnType<typeof xWorld>

const xStates = () => ({
  architecture: () => archState(7),
  'spec-lookup': () => skState('spec-lookup', 5, 2),
})

/** An architecture run at step 7 paused by spec-lookup, which has recorded a Read: where VER-44's unwinds start. */
async function xNested($: Any, x: XWorld) {
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await x.w.clock.advance(600)
  await x.load($, 'devforgeai:spec-lookup')
  await $.tool.call(READ('worked.md'))
}

const xTaskUpdate = (taskId: string, status: string, extra: Any = {}) => ({ tool: 'TaskUpdate', taskId, status, ...extra } as Any)

function xRuleEnds(w: World, skill: string): Any[] {
  return logOf(w, skill).filter(e => e.kind === 'run-end')
}

/** Position of the last write of a run's log: the order the run-ends were written in. */
function xLastWrite(w: World, skill: string): number {
  return w.writes.lastIndexOf(runFiles(w, 'events.jsonl').find(p => p.includes(`-${skill}-`))!)
}

// -- pausing --

test('VER-43: the band\'s row 1 ends with \' (paused: architecture at step 7)\'', async ($, on) => {
  const x = xWorld(on, xStates())
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await x.w.clock.advance(600)
  await x.load($, 'devforgeai:spec-lookup')
  await x.w.clock.advance(600)
  const ui = await ($ as Any).ui.mount({ ...BAND, surface: 'terminal' })
  const row = await ui.find({ type: 'Text', text: /spec-lookup/ })
  expect(row).toBeDefined()
  const text = String(row.children.join(''))
  expect(text).toContain('step 2 of 5: Step 2')
  expect(text.endsWith(' (paused: architecture at step 7)')).toBe(true)
  await ui.unmount()
})

test('VER-43: with three runs the band names the run just beneath and counts the rest', async ($, on) => {
  const x = xWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), ...xStates() })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await x.w.clock.advance(600)
  await x.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await x.w.clock.advance(600)
  await x.load($, 'devforgeai:spec-lookup')
  await x.w.clock.advance(600)
  expect(x.w.statuses[x.w.statuses.length - 1]).toBe('spec-lookup 2/5 · in architecture 7/11 · 1 more')
  const ui = await ($ as Any).ui.mount({ ...BAND, surface: 'terminal' })
  const row = await ui.find({ type: 'Text', text: /spec-lookup/ })
  const text = String(row.children.join(''))
  expect(text).toContain('(paused: architecture at step 7')
  expect(text).toContain('1 more')
  await ui.unmount()
})

test('VER-43: the status line names a paused run with no saved summary as \' · in <skill> <step>\'', async ($, on) => {
  const x = xWorld(on, xStates())
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)                           // no evaluation of architecture yet: its summary is empty
  await x.load($, 'devforgeai:spec-lookup')
  await x.w.clock.advance(600)
  expect(x.w.statuses[x.w.statuses.length - 1]).toBe('spec-lookup 2/5 · in architecture 7')
})

test('VER-43: a Read and a TaskCreate of the Skill call\'s batch record in the run open when each lands; the Skill call itself in neither', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await w.clock.advance(600)
  const read = $.tool.call({ tool: 'Read', file_path: `${ROOT}/docs/slow.md` } as Any)   // hook begun before the load
  await xSettle()
  let late: Any
  await x.load($, 'devforgeai:spec-lookup', {
    before: async () => {   // a TaskCreate under way: the load waits for it, but only up to 2 seconds (BEH-29)
      late = $.tool.call({ tool: 'TaskCreate', subject: '3. Late', description: 'd', metadata: { devforgeai_step: 3 } } as Any)
      await xSettle()
    },
    mid: async () => { await w.clock.advance(2000) },
  })
  expect(xRuleEnds(w, 'architecture')).toEqual([])
  expect(trailLog(w)).toEqual(['push architecture at step 7 (1 on the trail)'])
  x.release('create')
  x.release('read')
  await late
  await read
  const arch = logOf(w, 'architecture')
  const look = logOf(w, 'spec-lookup')
  expect(arch.filter(e => e.tool === 'TaskCreate').length).toBe(11)
  expect(arch.some(e => e.path === 'docs/slow.md')).toBe(false)
  expect(look.filter(e => e.tool === 'TaskCreate').length).toBe(1)
  expect(look.some(e => e.path === 'docs/slow.md')).toBe(true)
  // the new task is in spec-lookup's map: marking it gives a step event in that run, and unwinds nothing
  await $.tool.call(xTaskUpdate('50', 'in_progress'))
  expect(logOf(w, 'spec-lookup').some(e => e.kind === 'step' && e.step === 3 && e.state === 'started')).toBe(true)
  expect(logOf(w, 'architecture').some(e => e.kind === 'step' && e.step === 3)).toBe(false)
  expect(trailLog(w).length).toBe(1)
  expect([...arch, ...logOf(w, 'spec-lookup')].some(e => e.tool === 'Skill')).toBe(false)
})

test('VER-43: a TaskUpdate of the Skill call\'s batch, under way when the load comes, sets the return step', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 6)                       // the list marks step 6
  await w.clock.advance(600)
  const text = await x.load($, 'devforgeai:spec-lookup', {
    before: async () => {   // 'mark step 8 and load the skill' in one message
      void $.tool.call(xTaskUpdate('8', 'in_progress', { activeForm: 'HOLD-A' }))
      await xSettle()
    },
    mid: async () => { x.release('HOLD-A'); await xSettle() },
  })
  expect(text.endsWith('\n\n' + xReturn('architecture', 8))).toBe(true)
  expect(trailLog(w)).toEqual(['push architecture at step 8 (1 on the trail)'])
  expect(logOf(w, 'architecture').some(e => e.kind === 'step' && e.step === 8 && e.state === 'started')).toBe(true)
  expect(xRuleEnds(w, 'architecture')).toEqual([])
})

test('VER-43: a run with no known return step pauses nothing, and leaves the runs beneath paused', async ($, on) => {
  const x = xWorld(on, { brainstorm: () => skState('brainstorm', 8, 4),
    architecture: () => ({ ...STATE, skill: 'architecture', current: null, steps: [] }),
    'spec-lookup': () => skState('spec-lookup', 5, 2) })
  const { w } = x
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await w.clock.advance(600)
  await x.load($, 'devforgeai:architecture')         // brainstorm paused at step 4
  await $.tool.call({ tool: 'TaskCreate', subject: '1. Step 1', description: 'd', metadata: { devforgeai_step: 1 } } as Any)
  await w.clock.advance(600)                         // architecture: task IDs, but no step marked and none current
  expect(await x.load($, 'devforgeai:spec-lookup')).not.toContain('This skill was loaded by')
  expect(xRuleEnds(w, 'architecture').map(e => e.reason)).toEqual(['another-skill'])
  expect(xRuleEnds(w, 'brainstorm')).toEqual([])
  expect(trailLog(w)).toEqual(['push brainstorm at step 4 (1 on the trail)'])
  await $.tool.call(READ('n.md'))
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('spec-lookup 2/5 · in brainstorm 4/8')
})

test('VER-43: a second session.start keeps the trail and the open run, and the unwind still works', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await xNested($, x)
  const before = logOf(w, 'spec-lookup').slice(-1)[0].seq
  await start($)                                      // as after a reload (the kit can't reload the module itself)
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('spec-lookup 2/5 · in architecture 7/11')
  await $.tool.call(READ('after-reload.md'))
  expect(logOf(w, 'spec-lookup').slice(-1)[0]).toMatchObject({ kind: 'tool', path: 'docs/after-reload.md', seq: before + 1 })
  expect(runsOf(w, 'spec-lookup').length).toBe(1)
  expect(xRuleEnds(w, 'architecture')).toEqual([])
  await $.tool.call(xTaskUpdate('7', 'in_progress'))
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
})

test('VER-43: a load whose new run\'s log can\'t be written stops tracking (ERR-03): no push, no run-end, the trail emptied', async ($, on) => {
  const x = xWorld(on, xStates(), undefined, { failWrite: p => p.includes('-spec-lookup-') })
  const { w } = x
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await w.clock.advance(600)
  await x.load($, 'devforgeai:spec-lookup')
  expect(xRuleEnds(w, 'architecture')).toEqual([])
  expect(trailLog(w).some(l => l.startsWith('push'))).toBe(false)
  expect(w.statuses.includes('progress: off (cannot write devforgeai/progress)')).toBe(true)
})

// -- unwinding --

test('VER-44: a failed TaskUpdate of a paused run\'s task resumes nothing; completing task 7 resumes, in the next seq', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await w.clock.advance(600)
  const last = logOf(w, 'architecture').slice(-1)[0].seq
  await x.load($, 'devforgeai:spec-lookup')
  await $.tool.call(READ('worked.md'))
  await $.tool.call(xTaskUpdate('2', 'in_progress'))   // the stub fails this one: task 2 is architecture's
  expect(trailLog(w)).toEqual(['push architecture at step 7 (1 on the trail)'])
  expect(xRuleEnds(w, 'spec-lookup')).toEqual([])
  await $.tool.call(xTaskUpdate('7', 'completed'))
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
  expect(xRuleEnds(w, 'spec-lookup').map(e => e.reason)).toEqual(['returned'])
  const arch = logOf(w, 'architecture')
  expect(arch.slice(last).map(e => [e.seq, e.kind, e.tool ?? e.step, e.state ?? null])).toEqual([
    [last + 1, 'tool', 'TaskUpdate', null], [last + 2, 'step', 7, 'done'],
  ])
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('architecture 7/11')
})

test('VER-44: touching another of the paused run\'s tasks (task 8) resumes it', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await xNested($, x)
  await $.tool.call(xTaskUpdate('8', 'in_progress'))
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
  expect(xRuleEnds(w, 'spec-lookup').map(e => e.reason)).toEqual(['returned'])
  const arch = logOf(w, 'architecture')
  expect(arch.slice(-2).map(e => [e.kind, e.tool ?? e.step, e.state ?? null])).toEqual([['tool', 'TaskUpdate', null], ['step', 8, 'started']])
})

test('VER-44: a second TaskUpdate of a batch whose hooks began before the load resumes nothing', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 6)
  await w.clock.advance(600)
  // [TaskUpdate 7, TaskUpdate 8, Skill]: the first is recorded before the load, the second outlasts the wait
  const text = await x.load($, 'devforgeai:spec-lookup', {
    before: async () => {
      void $.tool.call(xTaskUpdate('7', 'in_progress', { activeForm: 'HOLD-A' }))
      void $.tool.call(xTaskUpdate('8', 'in_progress', { activeForm: 'HOLD-B' }))
      await xSettle()
    },
    mid: async () => { x.release('HOLD-A'); await xSettle(); await w.clock.advance(2000) },
  })
  expect(text.endsWith('\n\n' + xReturn('architecture', 7))).toBe(true)
  x.release('HOLD-B')
  await xSettle()
  await w.clock.advance(600)
  expect(trailLog(w)).toEqual(['push architecture at step 7 (1 on the trail)'])
  expect(xRuleEnds(w, 'architecture')).toEqual([])
  expect(xRuleEnds(w, 'spec-lookup')).toEqual([])
  expect(logOf(w, 'spec-lookup').some(e => e.tool === 'TaskUpdate')).toBe(true)   // recorded in the run open when it landed
  expect(w.statuses[w.statuses.length - 1]).toBe('spec-lookup 2/5 · in architecture 7/11')
  // the nested run works, and then the same TaskUpdate resumes: it was the batch that didn't count
  await $.tool.call(READ('worked.md'))
  await $.tool.call(xTaskUpdate('7', 'in_progress'))
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
})

test('VER-44: two TaskUpdates of the paused run\'s tasks in one batch resume it once', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await xNested($, x)
  await Promise.all([$.tool.call(xTaskUpdate('7', 'completed')), $.tool.call(xTaskUpdate('8', 'in_progress'))])
  expect(trailLog(w).filter(l => l.startsWith('unwind'))).toEqual(['unwind to architecture at step 7 (1 returned, 0 on the trail)'])
  expect(xRuleEnds(w, 'spec-lookup').length).toBe(1)
  expect(logOf(w, 'spec-lookup').filter(e => e.tool === 'TaskUpdate').length).toBe(0)
  expect(logOf(w, 'architecture').filter(e => e.tool === 'TaskUpdate').length).toBe(3)   // the mark, and these two
})

test('VER-44: a Claude load of a paused run\'s skill from two levels up unwinds to it, the runs above returned top first, no run opened', async ($, on) => {
  const x = xWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), ...xStates() })
  const { w } = x
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await x.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await x.load($, 'devforgeai:spec-lookup')
  expect(await x.load($, 'devforgeai:brainstorm')).not.toContain('This skill was loaded by')
  expect(runsOf(w, 'brainstorm').length).toBe(1)
  expect(xRuleEnds(w, 'spec-lookup').map(e => e.reason)).toEqual(['returned'])
  expect(xRuleEnds(w, 'architecture').map(e => e.reason)).toEqual(['returned'])
  expect(xRuleEnds(w, 'brainstorm')).toEqual([])
  expect(xLastWrite(w, 'spec-lookup') < xLastWrite(w, 'architecture')).toBe(true)
  expect(trailLog(w).slice(-1)[0].startsWith('unwind to brainstorm at step 4')).toBe(true)
  await $.tool.call(READ('b.md'))
  expect(logOf(w, 'brainstorm').slice(-1)[0]).toMatchObject({ kind: 'tool', path: 'docs/b.md' })
})

test('VER-44: an answered turn\'s end unwinds one level, whatever the runs beneath show', async ($, on) => {
  let inner: Any = skState('spec-lookup', 5, 2)
  let mid: Any = archState(7)
  const x = xWorld(on, { brainstorm: () => skState('brainstorm', 8, null), architecture: () => mid, 'spec-lookup': () => inner })
  const { w } = x
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await x.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await x.load($, 'devforgeai:spec-lookup')
  await $.tool.call(READ('one.md'))
  inner = skState('spec-lookup', 5, null)
  mid = archState(null)                               // architecture, once open, shows every step reached too
  await w.clock.advance(600)
  await turnEndFor($, 'answer')
  expect(trailLog(w).filter(l => l.startsWith('unwind'))).toEqual(['unwind to architecture at step 7 (1 returned, 1 on the trail)'])
  expect(xRuleEnds(w, 'brainstorm')).toEqual([])
  await w.clock.advance(600)
  await turnEndFor($, 'answer')
  const unwinds = trailLog(w).filter(l => l.startsWith('unwind'))
  expect(unwinds.length).toBe(2)
  expect(unwinds[1].startsWith('unwind to brainstorm at step 4')).toBe(true)
})

test('VER-44: returned runs with an item are reviewed top first, then the open run\'s own review, each answer in its own review.jsonl', async ($, on) => {
  const asked: string[] = []
  const rv = reviewing(['Accept', 'Accept', 'Accept'], asked)
  let spec: Any = skState('spec-lookup', 5, 2, null, [XSFLAG])   // returned before every step was reached
  let arch: Any = archState(7, null, [XAFLAG])
  let brain: Any = skState('brainstorm', 8, 4)
  const x = xWorld(on, { brainstorm: () => brain, architecture: () => arch, 'spec-lookup': () => spec },
    e => (e.questions?.[0]?.header === 'Review' ? rv(e) : undefined))
  const { w } = x
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await x.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await x.load($, 'devforgeai:spec-lookup')
  await $.tool.call(READ('r.md'))
  await $.tool.call(xTaskUpdate('4', 'in_progress'))     // Claude is back in brainstorm: both runs above return
  expect(trailLog(w).slice(-1)[0].startsWith('unwind to brainstorm at step 4')).toBe(true)
  brain = skState('brainstorm', 8, null, null, [XBFLAG])  // the open run has every step reached and an item too
  await w.clock.advance(600)
  await turnEnd($)
  const tail = ' (write gate): '
  expect(asked).toEqual([
    `spec-lookup run, item 1 of 1: flagged at step 3${tail}${XSFLAG.message}. Accept it, or challenge it?`,
    `architecture run, item 1 of 1: flagged at step 7${tail}${XAFLAG.message}. Accept it, or challenge it?`,
    `brainstorm run, item 1 of 1: flagged at step 2 (report gate): ${XBFLAG.message}. Accept it, or challenge it?`,
  ])
  const files = runFiles(w, 'review.jsonl')
  expect(files.length).toBe(3)
  for (const s of ['spec-lookup', 'architecture', 'brainstorm']) {
    const path = files.find(p => p.includes(`-${s}-`))
    expect(path).toBeDefined()
    expect(JSON.parse((w.files.get(path!) ?? '').split('\n').filter(Boolean)[0])).toMatchObject({ item: 1, answer: 'accept' })
  }
})

test('VER-44: a stopped nested run is reviewed at the turn\'s end from returned', async ($, on) => {
  const asked: string[] = []
  const rv = reviewing(['Accept'], asked)
  const x = xWorld(on, { brainstorm: () => skState('brainstorm', 8, 4),
    architecture: () => skState('architecture', 11, 8, null, [FLAG8], [8]) },
  e => (e.questions?.[0]?.header === 'Review' ? rv(e)
    : e.tool === 'AskUserQuestion' ? { result: { questions: e.questions, answers: { [e.questions[0].question]: 'Write nothing' }, annotations: {} } }
    : undefined))
  const { w } = x
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await x.load($, 'devforgeai:architecture')
  await w.clock.advance(600)
  const ask = { tool: 'AskUserQuestion', questions: [{ question: 'Outcome?', header: 'Step 8', multiSelect: false,
    options: [{ label: 'Confirm amend', description: 'd' }, { label: 'Write nothing', description: 'd' }] }], metadata: { source: 'devforgeai_step:8' } }
  await $.tool.call(ask as Any)
  expect(xRuleEnds(w, 'architecture').map(e => e.reason)).toEqual(['stopped'])
  expect(trailLog(w).slice(-1)[0].startsWith('unwind to brainstorm at step 4')).toBe(true)
  expect(asked).toEqual([])
  await turnEnd($)
  expect(asked).toEqual([`architecture run, item 1 of 1: flagged at step 8 (write gate): ${FLAG8.message}. Accept it, or challenge it?`])
  expect(runFiles(w, 'review.jsonl').some(p => p.includes('-architecture-'))).toBe(true)
})

test('VER-44: a returned run with no item asks nothing', async ($, on) => {
  const asked: string[] = []
  const rv = reviewing(['Accept'], asked)
  const x = xWorld(on, xStates(), e => (e.questions?.[0]?.header === 'Review' ? rv(e) : undefined))
  const { w } = x
  await xNested($, x)
  await $.tool.call(xTaskUpdate('7', 'in_progress'))
  expect(xRuleEnds(w, 'spec-lookup').map(e => e.reason)).toEqual(['returned'])
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked).toEqual([])
  expect(runFiles(w, 'review.jsonl')).toEqual([])
  expect(w.toasts.some(t => t.includes('your review is in'))).toBe(false)
})

test('VER-44: with nothing drawing, returned entries are dropped unreviewed, and a later surface doesn\'t revive them', async ($, on) => {
  const asked: string[] = []
  const rv = reviewing(['Accept'], asked)
  const over: Over = { surfaces: [] }
  const x = xWorld(on, { architecture: () => archState(7), 'spec-lookup': () => skState('spec-lookup', 5, 2, null, [XSFLAG]) },
    e => (e.questions?.[0]?.header === 'Review' ? rv(e) : undefined), over)
  const { w } = x
  await xNested($, x)
  await $.tool.call(xTaskUpdate('7', 'in_progress'))
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
  await w.clock.advance(600)
  await turnEnd($)
  expect(asked).toEqual([])
  expect(runFiles(w, 'review.jsonl')).toEqual([])
  over.surfaces = ['terminal']
  await start($)
  await turnEnd($)
  expect(asked).toEqual([])
  expect(runFiles(w, 'review.jsonl')).toEqual([])
})

test('VER-44: a full log while nested empties the trail, and the paused run gets no run-end', { timeoutMs: 120000 }, async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await xNested($, x)
  for (let i = 0; i < 200 && !w.statuses.includes('progress: off (event log full)'); i++) {
    await $.tool.call({ tool: 'Bash', command: 'z'.repeat(30 * 1024) } as Any)
  }
  expect(w.statuses.includes('progress: off (event log full)')).toBe(true)
  expect(trailLog(w).slice(-1)[0].startsWith('empty')).toBe(true)
  await $.tool.call(xTaskUpdate('7', 'in_progress'))
  expect(trailLog(w).some(l => l.startsWith('unwind'))).toBe(false)
  await $.session.end({ reason: 'prompt_input_exit', sessionId: 's1', resume: { id: 's1' } } as Any)
  expect(xRuleEnds(w, 'architecture')).toEqual([])
})

test('VER-44: /clear with a trail ends the paused runs and the open run with clear, bottom first', async ($, on) => {
  const x = xWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), ...xStates() })
  const { w } = x
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await x.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await x.load($, 'devforgeai:spec-lookup')
  await $.session.end({ reason: 'clear', sessionId: 's1', resume: { id: 's1' } } as Any)
  for (const s of ['brainstorm', 'architecture', 'spec-lookup']) expect(xRuleEnds(w, s).map(e => e.reason)).toEqual(['clear'])
  expect(xLastWrite(w, 'brainstorm') < xLastWrite(w, 'architecture') && xLastWrite(w, 'architecture') < xLastWrite(w, 'spec-lookup')).toBe(true)
})

test('VER-44: /clear while nested names the runs beneath, says done or has just started, and keeps with the nested text', async ($, on) => {
  const asked: string[] = []
  const ran: string[] = []
  let inner: Any = undefined  // the evaluator gives the new run no state yet
  const x = xWorld(on, { brainstorm: () => skState('brainstorm', 8, 4), architecture: () => archState(7), 'spec-lookup': () => inner },
    e => (e.questions?.[0]?.header === 'Progress' ? confirming('Keep working', asked)(e) : undefined))
  const { w } = x
  commands(on, ran)
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await taskList($, 8, 4)
  await x.load($, 'devforgeai:architecture')
  await taskList($, 11, 7, 9)
  await w.clock.advance(600)
  await x.load($, 'devforgeai:spec-lookup')           // no state for the new run yet
  const kept = (await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)) as Any
  expect(kept.text).toBe('Kept working: the spec-lookup run goes on, and architecture is still paused at step 7.')
  inner = skState('spec-lookup', 5, 2)
  await $.tool.call(READ('c.md'))
  await w.clock.advance(600)
  await $.command.run({ command: 'clear', args: '', origin: COMPOSER } as Any)
  inner = skState('spec-lookup', 5, null)
  await $.tool.call(READ('d.md'))
  await w.clock.advance(600)
  await $.command.run({ command: 'exit', args: '', origin: COMPOSER } as Any)
  expect(asked).toEqual([
    'spec-lookup run has just started (architecture paused at step 7, 1 more paused). Clear anyway?',
    'spec-lookup run is at step 2 of 5 and unfinished (architecture paused at step 7, 1 more paused). Clear anyway?',
    'spec-lookup run is done (architecture paused at step 7, 1 more paused). Exit anyway?',
  ])
  expect(ran).toEqual([])
})

// -- both modes --

test('VER-43/44: in enforce mode the run pauses, the nested run is checked on its own files, and the unwind resumes the outer', async ($, on) => {
  const x = xWorld(on, xStates(), undefined, { mode: 'enforce local' })
  const { w } = x
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await w.clock.advance(600)
  const text = await x.load($, 'devforgeai:spec-lookup')
  expect(text.endsWith('\n\n' + RETURN_7)).toBe(true)
  expect(xRuleEnds(w, 'architecture')).toEqual([])
  await $.tool.call({ tool: 'Write', file_path: `${ROOT}/docs/n.md`, content: 'x' } as Any)
  expect(runFiles(w, 'pending.jsonl').some(p => p.includes('-spec-lookup-'))).toBe(true)
  expect(runFiles(w, 'pending.jsonl').some(p => p.includes('-architecture-'))).toBe(false)
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('spec-lookup 2/5 · in architecture 7/11 · enforce')
  await $.tool.call(xTaskUpdate('7', 'completed'))
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
  expect(xRuleEnds(w, 'spec-lookup').map(e => e.reason)).toEqual(['returned'])
  await $.tool.call({ tool: 'Write', file_path: `${ROOT}/docs/m.md`, content: 'x' } as Any)
  expect(runFiles(w, 'pending.jsonl').some(p => p.includes('-architecture-'))).toBe(true)
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('architecture 7/11 · enforce')
})

test('VER-43/44: in observe mode the return line is added, nothing is refused, and the unwind works', async ($, on) => {
  const x = xWorld(on, xStates(), undefined, { mode: 'observe local' })
  const { w } = x
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await w.clock.advance(600)
  const text = await x.load($, 'devforgeai:spec-lookup')
  expect(text.endsWith('\n\n' + RETURN_7)).toBe(true)
  expect(xRuleEnds(w, 'architecture')).toEqual([])
  const r = (await $.tool.call({ tool: 'Write', file_path: `${ROOT}/docs/n.md`, content: 'x' } as Any)) as Any
  expect(r.deny).toBeUndefined()
  expect(runFiles(w, 'pending.jsonl')).toEqual([])
  await $.tool.call(xTaskUpdate('8', 'in_progress'))
  expect(trailLog(w).slice(-1)[0]).toBe('unwind to architecture at step 7 (1 returned, 0 on the trail)')
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('architecture 7/11')
})

// ---- after the build's adversarial review (2026-10-05) ----

test('VER-44: a paused run whose log can no longer be read: session.end still ends the open run and empties the module', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await xNested($, x)
  w.files.delete(runFiles(w, 'events.jsonl').find(p => p.includes('-architecture-'))!)   // its folder cleaned mid-session
  await $.session.end({ reason: 'clear', sessionId: 's1', resume: { id: 's1' } } as Any)
  expect(logOf(w, 'spec-lookup').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'clear' })
  await $.tool.call(READ('after-clear.md'))
  expect(logOf(w, 'spec-lookup').some(e => e.path === 'docs/after-clear.md')).toBe(false)
})

test('VER-43: a typed load while a paused run\'s log can\'t be read still ends the open run and opens the new one', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await xNested($, x)
  w.files.delete(runFiles(w, 'events.jsonl').find(p => p.includes('-architecture-'))!)
  await $.skill.prompt({ skill: 'devforgeai:prd', text: TAGGED })
  expect(runsOf(w, 'prd').length).toBe(1)
  expect(logOf(w, 'spec-lookup').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  expect(trailLog(w).slice(-1)[0]).toBe('empty (a load with no Skill call of the main loop in flight)')
})

test('VER-44: a TaskUpdate whose hook began before the load resumes nothing, even after the nested run has worked', async ($, on) => {
  const x = xWorld(on, xStates())
  const { w } = x
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 6)
  await w.clock.advance(600)
  let upd: Any
  await x.load($, 'devforgeai:spec-lookup', {
    before: async () => { upd = $.tool.call(xTaskUpdate('7', 'in_progress', { activeForm: 'HOLD-A' })); await xSettle() },
    mid: async () => { await w.clock.advance(2000) },   // the TaskUpdate outlasts the load's wait
  })
  await $.tool.call(READ('later.md'))                    // began after the load: spec-lookup has worked
  x.release('HOLD-A')
  await upd
  expect(trailLog(w).some(l => l.startsWith('unwind'))).toBe(false)
  expect(logOf(w, 'architecture').some(e => e.kind === 'run-end')).toBe(false)
})

// ---- version 15 (Bryan, 2026-10-05: "Fix now as v15"): Claude's load of the open run's own skill when it is finished ----

test('VER-43 (version 15): Claude\'s load of the open run\'s own skill changes nothing while it is unfinished; once every step is reached, a new run opens and the trail stays', async ($, on) => {
  let inner: Any = skState('spec-lookup', 5, 2)
  const { w, sk } = nestWorld(on, { architecture: () => archState(7), 'spec-lookup': () => inner })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await w.clock.advance(600)
  await sk.load($, 'devforgeai:spec-lookup')
  await $.tool.call(READ('a.md'))
  await w.clock.advance(600)
  expect(await sk.load($, 'devforgeai:spec-lookup')).not.toContain('This skill was loaded by')
  expect(runsOf(w, 'spec-lookup').length).toBe(1)                     // unfinished: nothing changes
  inner = skState('spec-lookup', 5, null)                              // every step reached
  await $.tool.call(READ('b.md'))
  await w.clock.advance(600)
  expect(await sk.load($, 'devforgeai:spec-lookup')).not.toContain('This skill was loaded by')
  expect(runsOf(w, 'spec-lookup').length).toBe(2)
  expect(logOf(w, 'spec-lookup', 0).slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  expect(logOf(w, 'architecture').some(e => e.kind === 'run-end')).toBe(false)   // still paused
  expect(trailLog(w)).toEqual(['push architecture at step 7 (1 on the trail)'])  // never a push
  inner = skState('spec-lookup', 5, 2)
  await $.tool.call(READ('c.md'))
  await w.clock.advance(600)
  expect(w.statuses[w.statuses.length - 1]).toBe('spec-lookup 2/5 · in architecture 7/11')
  expect(logOf(w, 'spec-lookup', 1).slice(-1)[0]).toMatchObject({ kind: 'tool', path: 'docs/c.md' })
})

test('VER-43 (version 15): Claude\'s load of a stopped run\'s own skill opens a new run', async ($, on) => {
  const { w, sk } = nestWorld(on, { architecture: () => skState('architecture', 11, 8, null, [], [8]) }, {},
    answering({ 'Outcome?': 'Write nothing' }))
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 8)
  await w.clock.advance(600)
  const ask = { tool: 'AskUserQuestion', questions: [{ question: 'Outcome?', header: 'Step 8', multiSelect: false,
    options: [{ label: 'Confirm amend', description: 'd' }, { label: 'Write nothing', description: 'd' }] }], metadata: { source: 'devforgeai_step:8' } }
  await $.tool.call(ask as Any)
  expect(logOf(w, 'architecture').slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'stopped' })
  expect(await sk.load($, 'devforgeai:architecture')).not.toContain('This skill was loaded by')
  expect(runsOf(w, 'architecture').length).toBe(2)
  expect(logOf(w, 'architecture', 0).filter(e => e.kind === 'run-end').map(e => e.reason)).toEqual(['stopped'])
  expect(trailLog(w)).toEqual([])
})

// ---- the module's values (version 14, DM-03): a hook that awaits across a run switch acts on the run open now ----

test('DM-03 (version 14): a tool call that started before a run switch records after it without reopening the old run', async ($, on) => {
  let releaseRead: (() => void) | null = null
  let releaseSkill: (() => void) | null = null
  const inner = taskTools()
  const w = world(on, { tool: (e: Any) => {
    if (e.tool === 'Read' && e.file_path?.endsWith('/slow.md')) return new Promise(r => { releaseRead = () => r({ result: 'read' }) })
    if (e.tool === 'Skill') return new Promise(r => { releaseSkill = () => r({ result: 'Launching skill' }) })
    return inner(e)
  } })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  const slow = $.tool.call({ tool: 'Read', file_path: `${ROOT}/docs/slow.md` } as Any)   // its dispatch starts now
  for (let i = 0; i < 50; i++) await Promise.resolve()
  const skill = $.tool.call({ tool: 'Skill', skill: 'devforgeai:spec-lookup' } as Any)
  for (let i = 0; i < 50; i++) await Promise.resolve()
  await $.skill.prompt({ skill: 'devforgeai:spec-lookup', text: TAGGED })              // the switch
  releaseSkill?.()
  await skill
  releaseRead?.()
  await slow                                                                            // records after the switch
  await $.tool.call({ tool: 'Read', file_path: `${ROOT}/docs/after.md` } as Any)
  const arch = runsOf(w, 'architecture')[0].map(l => JSON.parse(l))
  expect(arch[arch.length - 1]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })  // nothing after its end
  const look = eventsOf(w, 'spec-lookup').map(l => JSON.parse(l))
  expect(look[look.length - 1]).toMatchObject({ kind: 'tool', tool: 'Read', path: 'docs/after.md' })  // still the open run
})

// ---- version 16: the offer to continue an earlier run (VER-46 to VER-48) ----
// Helpers are prefixed r so they can't clash with the drafted file's.
//
// The kit's command.run is answered by a stub BELOW the mod (rWorld's on('command.run')): for a typed
// 'devforgeai:<skill>' it awaits $.skill.prompt({ skill, text }) before returning, so the skill.prompt fires inside the
// mod's next(e), as Claude Code does (probe, 2026-10-05). The stub records the order in r.order; the first test proves it.
// Earlier runs are files in the world's map (events.jsonl, a stale state.json); the evaluate stub answers by the
// --events path. Every dialog under header 'Progress' goes through world()'s over.tool (r.asked), never a second
// on('tool.call'). A control (a prd run offered and started fresh) gives each negative test a first assertion that
// fails while no offer is ever asked.

type RRows = { kinds: string[]; owned: number[] }
const R_ROWS: Record<string, RRows> = {
  brainstorm: { kinds: ['read', 'think', 'think', 'think', 'ask', 'forge', 'inspect', 'report'], owned: [5] },
  architecture: { kinds: ['read', 'read', 'read', 'read', 'read', 'think', 'ask', 'ask', 'forge', 'inspect', 'report'], owned: [7, 8] },
  prd: { kinds: ['read', 'think', 'ask', 'forge', 'report'], owned: [3] },
}

type RStateOpts = { evidence?: Record<number, string[]>; claims?: number[]; states?: Record<number, string>; ended?: string | null; manifest?: string }

/** A state of a skill with evidence and claims per step; a step is reached with evidence other than a waiver's, or a done claim. */
function rState(skill: string, o: RStateOpts = {}): Any {
  const rows = R_ROWS[skill]
  const reached = (n: number) => (o.evidence?.[n] ?? []).some(t => t !== 'waiver') || (o.claims ?? []).includes(n)
  let high = 0
  rows.kinds.forEach((_, i) => { if (reached(i + 1)) high = i + 1 })
  const current = high + 1 > rows.kinds.length ? null : high + 1
  const steps = rows.kinds.map((kind, i) => {
    const n = i + 1
    const evidence = (o.evidence?.[n] ?? []).map((type, j) => ({ seq: 10 + n * 2 + j, type, strength: 'strong', detail: 'x' }))
    return { n, title: `Step ${n}`, kind, need: 'required', userOwned: rows.owned.includes(n), ...(skill === 'architecture' && n === 8 ? { stoppable: true } : {}),
      state: o.states?.[n] ?? (reached(n) ? 'done' : n === current ? 'current' : 'pending'), evidence,
      claim: (o.claims ?? []).includes(n) ? { seq: 50 + n, state: 'done', reason: null } : null, note: '' }
  })
  return { ...STATE, run: 'r', skill, current, ended: o.ended ?? null, steps, flags: [],
    manifest: { ...STATE.manifest, state: o.manifest ?? 'matched' }, waiver: null }
}

/** A state with the steps `reached` done the usual way: read, answer, write and script evidence by kind, a done claim for think and report steps. */
function rReach(skill: string, reached: number[], o: RStateOpts & { waiver?: number[]; claimOnly?: number[]; none?: number[] } = {}): Any {
  const evidence: Record<number, string[]> = {}
  const claims: number[] = [...(o.claims ?? [])]
  const states: Record<number, string> = { ...(o.states ?? {}) }
  for (const n of reached) {
    const kind = R_ROWS[skill].kinds[n - 1]
    if (o.none?.includes(n)) continue
    if (o.waiver?.includes(n)) { evidence[n] = ['waiver']; states[n] = 'done'; continue }
    if (o.claimOnly?.includes(n) || kind === 'think' || kind === 'report') { claims.push(n); continue }
    evidence[n] = [kind === 'ask' ? 'answer' : kind === 'forge' ? 'write' : kind === 'inspect' ? 'script' : 'read']
  }
  return rState(skill, { ...o, evidence: { ...evidence, ...(o.evidence ?? {}) }, claims, states })
}

/** What the evaluator gives a run nobody set up: nothing reached yet. */
function rFresh(skill: string): Any {
  return R_ROWS[skill] ? rState(skill) : skState(skill, 5, 2)
}

type RFix = {
  skill: string; stamp?: string; hex?: string; last?: string; end?: string | null; marked?: number | null
  writes?: Array<string | { path: string; failed?: boolean; tool?: string }>; state?: Any; evalFail?: boolean; noLog?: boolean
}

const rIdOf = (f: RFix) => `${f.stamp ?? '20260930T090000Z'}-${f.skill}-${f.hex ?? '1a2b3c4d'}`
const rIso = (stamp: string) => `${stamp.slice(0, 4)}-${stamp.slice(4, 6)}-${stamp.slice(6, 8)}T${stamp.slice(9, 11)}:${stamp.slice(11, 13)}:${stamp.slice(13, 15)}Z`

/** An earlier run's events.jsonl: skill-loaded, its Write/Edit events, the step the task list marked, run-end. */
function rLines(f: RFix): string {
  const run = rIdOf(f)
  const start = rIso(f.stamp ?? '20260930T090000Z')
  const last = f.last ?? '2026-09-30T10:00:00Z'
  const out: Any[] = [{ kind: 'skill-loaded', format: 'devforgeai-events/1', skill: f.skill, checklist: TAGGED, host: 'claude-code 2.1.287',
    taskList: true, mode: 'observe', modeSource: 'framework-default', time: start }]
  for (const w of f.writes ?? []) {
    const x = typeof w === 'string' ? { path: w } : w
    out.push({ kind: 'tool', tool: (x as Any).tool ?? 'Write', path: x.path, exit: (x as Any).failed ? null : 0, error: !!(x as Any).failed, content: 'x', time: last })
  }
  if (f.marked) out.push({ kind: 'step', step: f.marked, state: 'started', time: last })
  if (f.end) out.push({ kind: 'run-end', reason: f.end, time: last })
  return out.map((e, i) => JSON.stringify({ run, seq: i + 1, ...{ time: e.time, ...e } })).join('\n') + '\n'
}

/** A brainstorm run that ended at `marked`, with steps 1 to marked-1 reached; override per test. */
function rBrn(marked: number, o: Partial<RFix> = {}, reached?: number[], so: Parameters<typeof rReach>[2] = {}): RFix {
  const upto = reached ?? Array.from({ length: marked - 1 }, (_, i) => i + 1)
  return { skill: 'brainstorm', end: 'session-end', marked, state: rReach('brainstorm', upto, so), ...o }
}

type RCfg = {
  earlier?: RFix[]
  answer?: string | null | 'FAIL' | ((q: Any) => string)
  mode?: string
  surfaces?: string[]
  noLoad?: string[]
  stateOf?: (skill: string, ev: string) => Any | undefined
  control?: boolean
}

const R_CONTINUE = (q: Any): string => q.options[0].label
const R_DISMISS = { result: "Error: The user doesn't want to proceed with this tool use.", isError: true, text: "The user doesn't want to proceed with this tool use." }

/** The world for the offer: earlier runs as files, the Progress dialog answered, a command.run stub whose skill.prompt fires inside next(e). */
function rWorld($top: Any, on: Any, cfg: RCfg = {}) {
  const files: Record<string, string> = {}
  const fixtures: RFix[] = [...(cfg.earlier ?? [])]
  const control: RFix = { skill: 'prd', stamp: '20260929T090000Z', hex: '0f0f0f0f', end: 'session-end', marked: 3, state: rReach('prd', [1, 2]) }
  if (cfg.control !== false) fixtures.push(control)
  for (const f of fixtures) {
    const dir = `${PROGRESS}/runs/${rIdOf(f)}`
    if (!f.noLog) files[`${dir}/events.jsonl`] = rLines(f)
    files[`${dir}/state.json`] = '{}'
  }
  const sk = skillCalls()
  const r = {
    asked: [] as Array<{ header: string; question: string; options: string[] }>,
    controlAsked: [] as string[], order: [] as string[], texts: [] as string[], ran: [] as string[], atAsk: [] as number[],
    ids: (cfg.earlier ?? []).map(rIdOf), sk, cfg, over: {} as Over, w: null as unknown as World,
  }
  const over: Over = {
    files, surfaces: cfg.surfaces, mode: cfg.mode,
    tool: (e: Any): Any => {
      const q = e.questions?.[0]
      if (e.tool === 'AskUserQuestion' && q?.header === 'Progress' && String(q.question).startsWith('prd: ')) {
        r.controlAsked.push(q.question)
        return { result: { questions: e.questions, answers: { [q.question]: 'Start fresh' }, annotations: {} } }
      }
      if (e.tool === 'AskUserQuestion' && q?.header === 'Progress' && String(q.question).includes('Continue it?')) {
        r.asked.push({ header: q.header, question: q.question, options: q.options.map((o: Any) => o.label) })
        r.order.push('dialog')
        r.atAsk.push(r.w ? runsOf(r.w, 'brainstorm').length : -1)
        const a = cfg.answer === undefined ? R_CONTINUE : cfg.answer
        if (a === 'FAIL') return { deny: 'the dialog could not be shown' }
        if (a === null) return R_DISMISS
        const label = typeof a === 'function' ? a(q) : a
        return { result: { questions: e.questions, answers: { [q.question]: label }, annotations: {} } }
      }
      if (e.tool === 'AskUserQuestion' && q?.question === 'Outcome?') return answering({ 'Outcome?': 'Write nothing' })(e)
      return sk.tool(e)
    },
    evaluate: (argv: readonly string[]) => {
      // The plugin's manifests, where the adapter reads the step with the write gate (BEH-31): brainstorm's 6,
      // architecture's 9, under the plugin root the evaluator's own path gives (the offer evaluates first).
      const pluginRoot = String(argv[1]).replace(/\/progress\/evaluate\.py$/, '')
      for (const [skill, gate] of [['brainstorm', 6], ['architecture', 9]] as const) {
        r.w?.files.set(`${pluginRoot}/progress/manifests/${skill}.json`, JSON.stringify({ skill, steps: { [gate]: { gate: 'write' } } }))
      }
      const ev = argv[argv.indexOf('--events') + 1]
      const skill = ev.match(/runs\/[0-9]{8}T[0-9]{6}Z-([a-z][a-z0-9-]*)-[0-9a-f]{8}\//)?.[1] ?? 'brainstorm'
      const fix = fixtures.find(f => ev.includes(`/${rIdOf(f)}/`))
      if (fix?.evalFail) return { deny: 'evaluator unavailable' }
      if (fix?.state) return { state: fix.state }
      if (fix === control) return { state: control.state }
      return { state: cfg.stateOf?.(skill, ev) ?? rFresh(skill) }
    },
  }
  r.over = over
  r.w = world(on, over)
  on('command.run', async (_$: Any, e: Any) => {
    r.order.push('command:enter')
    r.ran.push(e.command)
    if (!(cfg.noLoad ?? []).includes(e.command) && String(e.command).startsWith('devforgeai:')) {
      r.order.push('skill.prompt')
      const out = (await $top.skill.prompt({ skill: e.command, text: TAGGED })) as Any
      r.texts.push(out.text)
      r.order.push('skill.prompt done')
    }
    r.order.push('command:leave')
    return { text: `ran ${e.command}` }
  })
  return r
}

type RW = ReturnType<typeof rWorld>

const rType = ($: Any, skill = 'brainstorm') => $.command.run({ command: `devforgeai:${skill}`, args: '', origin: COMPOSER } as Any) as Promise<Any>

/** The control: a prd run is offered and started fresh. Fails while no offer is asked, so the negatives below fail for the right reason. */
async function rControl($: Any, r: RW) {
  await rType($, 'prd')
  expect(r.controlAsked).toEqual(['prd: an earlier run ended at step 3 of 5 on 2026-09-30 (session end). It wrote nothing. Continue it?'])
  rControlLines.set(r.w, rLog(r.w).length)  // its 'fresh (Start fresh)' line names no run: skip the control's lines by count
}

const rControlLines = new WeakMap<World, number>()

/** adapter.log's lines of kind resume. */
function rLog(w: World): string[] {
  return (w.files.get(`${SESSION}/adapter.log`) ?? '').split('\n').filter(l => l.includes(' resume: ')).map(l => l.split(' resume: ')[1])
}

/** adapter.log's resume lines, less the control's. */
const rLogOf = (r: RW): string[] => rLog(r.w).slice(rControlLines.get(r.w) ?? 0).filter(l => !l.includes('-prd-0f0f0f0f'))

const rNew = (r: RW, skill = 'brainstorm', i = 0): Any => logOf(r.w, skill, i)[0]

const rQuestion = (skill: string, when: string, files = 'nothing') => `${skill}: an earlier run ${when}. It wrote ${files}. Continue it?`
const R_ENDED_4 = 'ended at step 4 of 8 on 2026-09-30 (session end)'

/** BEH-31's line, from its template; lists are joined with ', ' (the template doesn't say how a list is written). */
function rLineOf(o: { skill?: string; id: string; when: string; carried: number[]; step: number; owned?: number[]; files?: string[] }): string {
  const skill = o.skill ?? 'brainstorm'
  return `This run continues the earlier ${skill} run ${o.id}, which ${o.when}. Steps ${o.carried.join(', ')} are carried over: the tracker counts them reached. `
    + `Create the task list with those steps' tasks completed, mark step ${o.step} in progress, and continue at step ${o.step}.`
    + ((o.owned ?? []).length ? ` Steps ${o.owned!.join(', ')} were the user's decisions, which the record doesn't keep: before any document records them, confirm each with the user again, in order, marking its step in progress and tagging the question with it.` : '')
    + ` Files it wrote: ${(o.files ?? []).length ? o.files!.join(', ') : 'nothing'}. Its replies are in devforgeai/progress/runs/${o.id}/events.jsonl, the events of kind reply: use them to show the user what was proposed, never as a decision.`
}

/** The text Claude read ends with the line, and nothing but the line was added to the checklist. */
function rExpectLine(text: string, line: string) {
  expect(text.startsWith(TAGGED)).toBe(true)
  expect(text.slice(TAGGED.length).trim()).toBe(line)
}

const BRN = 'docs/specs/brainstorm/BRN-001.md'

// -- VER-46: the offer and Continue --

test('VER-46: a typed load asks the offer inside command.run\'s next(e), before the run opens (header Progress, the two options)', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)], answer: 'Start fresh' })
  await start($)
  await rType($)
  expect(r.asked).toEqual([{ header: 'Progress', question: rQuestion('brainstorm', R_ENDED_4), options: ['Continue from step 4', 'Start fresh'] }])
  // skill.prompt fired inside command.run's next(e), the dialog was asked in it, and no run existed yet
  expect(r.order).toEqual(['command:enter', 'skill.prompt', 'dialog', 'skill.prompt done', 'command:leave'])
  expect(r.atAsk).toEqual([0])
})

test('VER-46: the earlier run is evaluated once more from its own log before it is offered', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await rType($)
  const id = r.ids[0]
  const evaluated = r.w.runs.filter(a => a[2] === 'evaluate' && a.some(x => x.includes(`/${id}/`)))
  expect(evaluated.length).toBeGreaterThan(0)
  expect(evaluated[0].some(x => x.endsWith(`/${id}/state.json`))).toBe(true)
  expect(r.w.files.get(`${PROGRESS}/runs/${id}/state.json`)).not.toBe('{}')
})

test('VER-46: Continue opens the run with resumes and carried 1 to 3 on its skill-loaded event', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await rType($)
  const loaded = rNew(r)
  expect(loaded).toMatchObject({ kind: 'skill-loaded', skill: 'brainstorm', resumes: r.ids[0], carried: [1, 2, 3] })
  expect(loaded.answered ?? []).toEqual([])
  expect(loaded.checklist).not.toContain('This run continues')
  expect(loaded.checklist).toContain('1. Intake')
})

test('VER-46: Continue gives the text Claude reads BEH-31\'s line at its end, and the skill-loaded checklist doesn\'t have it', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await rType($)
  rExpectLine(r.texts[0], rLineOf({ id: r.ids[0], when: R_ENDED_4, carried: [1, 2, 3], step: 4 }))
  expect(rNew(r).checklist.includes('This run continues')).toBe(false)
})

test('VER-46: adapter.log gets an offered line and a continued line', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await rType($)
  const log = rLog(r.w)
  expect(log[0]).toBe(`offered ${r.ids[0]} at step 4`)
  expect(log[1]).toBe(`continued ${r.ids[0]} at step 4, carried 1, 2, 3`)
})

test('VER-46: the band shows carried steps as ◉', async ($, on) => {
  const carried = rState('brainstorm', { evidence: { 1: ['carried'], 2: ['carried'], 3: ['carried'] }, states: { 1: 'carried', 2: 'carried', 3: 'carried' } })
  const w = world(on, { evaluate: () => ({ state: carried }) })
  await start($)
  await load($, 'devforgeai:brainstorm', TAGGED)
  await w.clock.advance(600)
  const ui = await ($ as Any).ui.mount({ ...BAND, surface: 'terminal' })
  const row = await ui.find({ type: 'Text', text: /brainstorm/ })
  expect(row).toBeDefined()
  expect(String(row.children.join(''))).toContain('◉◉◉◆○○○○')
  await ui.unmount()
})

test('VER-46: a run marked at step 6 carries 1 to 5, and the line names step 5 to confirm again', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(6)] })
  await start($)
  await rType($)
  expect(r.asked.map(a => a.options[0])).toEqual(['Continue from step 6'])
  expect(rNew(r)).toMatchObject({ resumes: r.ids[0], carried: [1, 2, 3, 4, 5] })
  expect(rNew(r).answered ?? []).toEqual([])
  const when = 'ended at step 6 of 8 on 2026-09-30 (session end)'
  rExpectLine(r.texts[0], rLineOf({ id: r.ids[0], when, carried: [1, 2, 3, 4, 5], step: 6, owned: [5] }))
})

test('VER-46: a run at step 7 whose BRN was written after the user answered step 5 carries 1 to 6 with answered [5] and leaves the sentence out', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(7, { writes: [BRN] }, [1, 2, 3, 4, 5, 6])] })
  await start($)
  await rType($)
  expect(r.asked.map(a => a.options[0])).toEqual(['Continue from step 7'])
  const loaded = rNew(r)
  expect(loaded).toMatchObject({ resumes: r.ids[0], carried: [1, 2, 3, 4, 5, 6], answered: [5] })
  const when = 'ended at step 7 of 8 on 2026-09-30 (session end)'
  rExpectLine(r.texts[0], rLineOf({ id: r.ids[0], when, carried: [1, 2, 3, 4, 5, 6], step: 7, owned: [], files: [BRN] }))
  expect(r.texts[0]).not.toContain("were the user's decisions")
})

test('VER-46: a BRN written under Proceed with step 5 unanswered gives answered empty, and the line names step 5 to confirm again', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(7, { writes: [BRN] }, [1, 2, 3, 4, 5, 6], { waiver: [5] })] })
  await start($)
  await rType($)
  const loaded = rNew(r)
  expect(loaded).toMatchObject({ resumes: r.ids[0], carried: [1, 2, 3, 4, 5, 6] })
  expect(loaded.answered ?? []).toEqual([])
  const when = 'ended at step 7 of 8 on 2026-09-30 (session end)'
  rExpectLine(r.texts[0], rLineOf({ id: r.ids[0], when, carried: [1, 2, 3, 4, 5, 6], step: 7, owned: [5], files: [BRN] }))
})

test('VER-46: the dialog and the line name the files its writes that didn\'t fail wrote, each once, in order', async ($, on) => {
  const writes = [BRN, { path: 'docs/failed.md', failed: true }, 'docs/notes.md', { path: BRN, tool: 'Edit' }, { path: 'docs/edit-failed.md', failed: true, tool: 'Edit' }]
  const r = rWorld($, on, { earlier: [rBrn(7, { writes }, [1, 2, 3, 4, 5, 6])] })
  await start($)
  await rType($)
  const files = `${BRN}, docs/notes.md`
  expect(r.asked.map(a => a.question)).toEqual([rQuestion('brainstorm', 'ended at step 7 of 8 on 2026-09-30 (session end)', files)])
  expect(r.texts[0]).toContain(`Files it wrote: ${files}. Its replies are in devforgeai/progress/runs/${r.ids[0]}/events.jsonl`)
})

for (const mode of ['observe framework-default', 'enforce local']) {
  test(`VER-46: the offer and Continue are the same in ${mode.split(' ')[0]} mode`, async ($, on) => {
    const r = rWorld($, on, { earlier: [rBrn(4)], mode })
    await start($)
    await rType($)
    expect(r.asked.length).toBe(1)
    expect(rNew(r)).toMatchObject({ mode: mode.split(' ')[0], resumes: r.ids[0], carried: [1, 2, 3] })
    rExpectLine(r.texts[0], rLineOf({ id: r.ids[0], when: R_ENDED_4, carried: [1, 2, 3], step: 4 }))
  })
}

for (const [reason, why] of [['clear', '/clear'], ['stopped', 'stopped'], ['another-skill', 'another skill loaded'], ['returned', 'returned to the skill beneath']]) {
  test(`VER-46: a run that ended with run-end ${reason} is offered with (${why})`, async ($, on) => {
    const r = rWorld($, on, { earlier: [rBrn(4, { end: reason })] })
    await start($)
    await rType($)
    expect(r.asked.map(a => a.question)).toEqual([rQuestion('brainstorm', `ended at step 4 of 8 on 2026-09-30 (${why})`)])
  })
}

// -- VER-47: which run is offered --

test('VER-47: only the skill\'s latest run: an older unfinished run behind a newer finished one isn\'t offered', async ($, on) => {
  const older = rBrn(4, { stamp: '20260929T090000Z', hex: 'aaaaaaaa' })
  const newer = rBrn(8, { stamp: '20260930T090000Z', hex: 'bbbbbbbb' }, [1, 2, 3, 4, 5, 6, 7, 8])
  const r = rWorld($, on, { earlier: [older, newer] })
  await start($)
  await rControl($, r)
  await rType($)
  expect(r.asked).toEqual([])
  expect(rNew(r).resumes).toBeUndefined()
})

test('VER-47: a run whose last step is reached isn\'t offered', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(8, {}, [1, 2, 3, 4, 5, 6, 7, 8])] })
  await start($)
  await rControl($, r)
  await rType($)
  expect(r.asked).toEqual([])
  expect(rLog(r.w).filter(l => l.startsWith('continued'))).toEqual([])
})

test('VER-47: a stopped architecture run is offered at its stop step, also when it is this session\'s open run', async ($, on) => {
  let stopped = false
  const r = rWorld($, on, { stateOf: skill => (skill === 'architecture' ? rReach('architecture', [1, 2, 3, 4, 5, 6, 7], stopped ? { ended: 'stopped' } : {}) : undefined) })
  await start($)
  await rType($, 'architecture')
  await taskList($, 11, 8)
  await r.w.clock.advance(600)
  stopped = true
  await $.tool.call(stepQuestion('Outcome?') as Any)
  expect(runEnds(r.w)).toEqual(['stopped'])
  await rType($, 'architecture')
  expect(r.asked.map(a => [a.question, a.options])).toEqual([[
    rQuestion('architecture', 'ended at step 8 of 11 on 2026-10-02 (stopped)'), ['Continue from step 8', 'Start fresh']]])
  expect(logOf(r.w, 'architecture', 1)[0]).toMatchObject({ kind: 'skill-loaded', carried: [1, 2, 3, 4, 5, 6, 7] })
  expect(logOf(r.w, 'architecture', 1)[0].resumes).toMatch(/^20261002T120000Z-architecture-[0-9a-f]{8}$/)
})

test('VER-47: this session\'s open run that hasn\'t ended isn\'t offered (a skill typed again mid-run is a restart)', async ($, on) => {
  const r = rWorld($, on, { stateOf: skill => (skill === 'brainstorm' ? rReach('brainstorm', [1, 2, 3]) : undefined) })
  await start($)
  await rControl($, r)
  await rType($)
  await taskList($, 8, 4)
  await r.w.clock.advance(600)
  await rType($)
  expect(r.asked).toEqual([])
  expect(runsOf(r.w, 'brainstorm').length).toBe(2)
  expect(logOf(r.w, 'brainstorm', 0).slice(-1)[0]).toMatchObject({ kind: 'run-end', reason: 'another-skill' })
  expect(logOf(r.w, 'brainstorm', 1)[0].resumes).toBeUndefined()
})

test('VER-47: a run paused on the trail isn\'t offered', async ($, on) => {
  const r = rWorld($, on, { stateOf: skill => (skill === 'architecture' ? rReach('architecture', [1, 2, 3, 4, 5, 6]) : undefined) })
  await start($)
  await rControl($, r)
  await rType($, 'architecture')
  await taskList($, 11, 7)
  await r.w.clock.advance(600)
  await r.sk.load($, 'devforgeai:spec-lookup')
  expect(trailLog(r.w)).toEqual(['push architecture at step 7 (1 on the trail)'])
  await rType($, 'architecture')
  expect(r.asked).toEqual([])
  expect(runsOf(r.w, 'architecture').length).toBe(2)
  expect(logOf(r.w, 'architecture', 1)[0].resumes).toBeUndefined()
})

for (const [last, age] of [['2026-10-02T11:55:00Z', '5 minutes'], ['2026-10-02T09:00:00Z', '3 hours'], ['2026-09-30T12:00:00Z', '2 days']]) {
  test(`VER-47: a run with no run-end is offered at once, its last event ${age} ago`, async ($, on) => {
    const r = rWorld($, on, { earlier: [rBrn(4, { end: null, last })] })
    await start($)
    await rType($)
    const when = `was at step 4 of 8 with no end recorded, its last event ${age} ago (it may still be open in another session)`
    expect(r.asked.map(a => a.question)).toEqual([rQuestion('brainstorm', when)])
    expect(r.asked[0].options).toEqual(['Continue from step 4', 'Start fresh'])
    rExpectLine(r.texts[0], rLineOf({ id: r.ids[0], when, carried: [1, 2, 3], step: 4 }))
  })
}

for (const manifest of ['stale', 'none']) {
  test(`VER-47: a run whose manifest state is ${manifest}, not matched, isn't offered`, async ($, on) => {
    const r = rWorld($, on, { earlier: [rBrn(4, {}, undefined, { manifest })] })
    await start($)
    await rControl($, r)
    await rType($)
    expect(r.asked).toEqual([])
    expect(rNew(r).resumes).toBeUndefined()
  })
}

test('VER-47: a run with nothing to carry (at step 1) isn\'t offered', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(1, { marked: null }, [])] })
  await start($)
  await rControl($, r)
  await rType($)
  expect(r.asked).toEqual([])
  expect(rNew(r).resumes).toBeUndefined()
})

test('VER-47: a run marked past a write-gate step with no write evidence continues at that step, also when it was only claimed done', async ($, on) => {
  // step 6 (the BRN's write gate) was claimed done, never evidenced; the task list marked step 8
  const r = rWorld($, on, { earlier: [rBrn(8, {}, [1, 2, 3, 4, 5, 6, 7], { claimOnly: [6] })] })
  await start($)
  await rType($)
  expect(r.asked.map(a => a.options)).toEqual([['Continue from step 6', 'Start fresh']])
  expect(rNew(r)).toMatchObject({ resumes: r.ids[0], carried: [1, 2, 3, 4, 5] })
  expect(rNew(r).answered ?? []).toEqual([])
  const when = r.asked[0].question.match(/an earlier run (ended at step \d of 8 on 2026-09-30 \(session end\))\./)![1]
  rExpectLine(r.texts[0], rLineOf({ id: r.ids[0], when, carried: [1, 2, 3, 4, 5], step: 6, owned: [5] }))   // its decision before it stays to be confirmed again
})

test('VER-47: a run marked past a write-gate step nothing was claimed for continues at that step too', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(7, {}, [1, 2, 3, 4, 5], {})] })
  await start($)
  await rType($)
  expect(r.asked.map(a => a.options)).toEqual([['Continue from step 6', 'Start fresh']])
  expect(rNew(r)).toMatchObject({ carried: [1, 2, 3, 4, 5] })
})

// -- VER-48: declining and failing --

/** A run opened without the offer: neither field, nothing after the checklist. */
function rPlain(r: RW, t = 0) {
  const loaded = rNew(r)
  expect(loaded.kind).toBe('skill-loaded')
  expect(loaded.resumes).toBeUndefined()
  expect(loaded.carried).toBeUndefined()
  expect(loaded.answered).toBeUndefined()
  expect(r.texts[t]).toBe(TAGGED)
}

test('VER-48: Start fresh opens the run with neither field and no line, and adapter.log has fresh', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)], answer: 'Start fresh' })
  await start($)
  await rType($)
  expect(r.asked.length).toBe(1)
  rPlain(r)
  const log = rLog(r.w)
  expect(log[0]).toBe(`offered ${r.ids[0]} at step 4`)
  expect(log[1]).toMatch(/^fresh \(.+\)$/)
})

test('VER-48: Esc (a dismissal) opens the run as before, and adapter.log has fresh', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)], answer: null })
  await start($)
  await rType($)
  expect(r.asked.length).toBe(1)
  rPlain(r)
  expect(rLog(r.w)[1]).toMatch(/^fresh \(.+\)$/)
})

test('VER-48: nothing drawing offers nothing', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await rControl($, r)
  r.over.surfaces = []
  await rType($)
  expect(r.asked).toEqual([])
  expect(rLogOf(r)).toEqual([])
  rPlain(r, 1)
})

test('VER-48: an evaluator failure on the earlier run offers nothing, the run opens, one resume line says why', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4, { evalFail: true })] })
  await start($)
  await rControl($, r)
  await rType($)
  expect(r.asked).toEqual([])
  rPlain(r, 1)
  expect(rLogOf(r).length).toBe(1)
})

test('VER-48: an unreadable log (a run folder with no events.jsonl) offers nothing, the run opens, one resume line says why', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4, { noLog: true })] })
  await start($)
  await rControl($, r)
  await rType($)
  expect(r.asked).toEqual([])
  rPlain(r, 1)
  expect(rLogOf(r).length).toBe(1)
})

test('VER-48: a dialog that fails is taken as Start fresh: the run opens with neither field and no line (ERR-17)', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)], answer: 'FAIL' })
  await start($)
  await rType($)
  expect(r.asked.length).toBe(1)
  rPlain(r)
  const log = rLog(r.w)
  expect(log.length).toBeGreaterThan(0)
  expect(log.filter(l => l.startsWith('continued'))).toEqual([])
})

test('VER-48: a load by Claude\'s Skill tool while a run is open and unfinished (the control\'s) offers nothing', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await rControl($, r)
  const text = await r.sk.load($, 'devforgeai:brainstorm')
  expect(r.asked).toEqual([])
  expect(text).not.toContain('This run continues')
  expect(rNew(r).resumes).toBeUndefined()
})

test('VER-48: a skill.prompt with no command.run around it offers nothing', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await rControl($, r)
  const out = (await $.skill.prompt({ skill: 'devforgeai:brainstorm', text: TAGGED })) as Any
  expect(r.asked).toEqual([])
  expect(out.text).toBe(TAGGED)
  expect(rNew(r).resumes).toBeUndefined()
})

test('VER-48: a typed load while a Skill call of the main loop is in flight offers nothing', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await rControl($, r)
  await r.sk.load($, 'devforgeai:brainstorm', undefined, async () => { await rType($) })   // the Skill call is held open while command.run runs
  expect(r.asked).toEqual([])
  expect(rNew(r).resumes).toBeUndefined()
})

test('VER-48: a command.run that loads no skill asks nothing, and a later typed load isn\'t taken for it', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)], noLoad: ['devforgeai:brainstorm'] })
  await start($)
  await rControl($, r)
  await $.command.run({ command: 'devforgeai:brainstorm', args: '', origin: COMPOSER } as Any)   // loads no skill here
  await $.command.run({ command: 'help', args: '', origin: COMPOSER } as Any)
  expect(r.asked).toEqual([])
  const out = (await $.skill.prompt({ skill: 'devforgeai:brainstorm', text: TAGGED })) as Any   // not around a command.run
  expect(r.asked).toEqual([])
  expect(out.text).toBe(TAGGED)
  expect(rNew(r).resumes).toBeUndefined()
})

// ---- version 17 (Bryan, 2026-10-05: "Fix now as v17"): Claude's load with no unfinished run open is offered too ----

test('VER-48 (version 17): a load by Claude\'s Skill tool with no run open is offered, and Continue carries as a typed load does', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  const text = await r.sk.load($, 'devforgeai:brainstorm')
  expect(r.asked.map(a => a.options)).toEqual([['Continue from step 4', 'Start fresh']])
  expect(rNew(r)).toMatchObject({ resumes: r.ids[0], carried: [1, 2, 3] })
  expect(text).toContain('This run continues the earlier brainstorm run ' + r.ids[0])
  expect(rLogOf(r).filter(l => l.startsWith('continued '))).toEqual([`continued ${r.ids[0]} at step 4, carried 1, 2, 3`])
})

test('VER-48 (version 17): a load by Claude\'s Skill tool while a run is paused on the trail offers nothing', async ($, on) => {
  const r = rWorld($, on, { earlier: [rBrn(4)] })
  await start($)
  await load($, 'devforgeai:architecture', TAGGED)
  await taskList($, 11, 7)
  await r.w.clock.advance(600)
  await r.sk.load($, 'devforgeai:spec-lookup')    // architecture paused on the trail
  await r.sk.load($, 'devforgeai:brainstorm')
  expect(r.asked).toEqual([])
})
