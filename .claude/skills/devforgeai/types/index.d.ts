// The progress tracker adapter's $.state contract (SPEC-013 v27 DM-03; version 27 adds no key: a hold of BEH-42 lives in the module's
// memory with the lines it keeps, since a reload loses those lines and a count that outlived them would describe nothing). $.state survives a reload of the module
// and empties on /clear, /resume and /branch; whether the session is interactive and the evaluation timer are
// module variables instead (BEH-01, BEH-06).

/** The open run: its ID, skill, the last seq used, its folder under devforgeai/progress/runs/, and the root it
 *  opened in, which its paths, manifests and files use (BEH-03). Its event lines
 *  live in the module and events.jsonl, not here: one $.state value holds at most 4,194,304 characters, which a
 *  run's lines can pass before the log reaches its 4 MiB (found by the build's ERR-11 test). */
export type ProgressRun = {
  id: string
  skill: string
  seq: number
  dir: string
  root: string
}

/** What the status line and the band draw, taken from the last evaluation (SPEC-012 DM-03). */
export type ProgressSummary = {
  skill: string
  current: number | null
  steps: number
  flags: number
  yourTurn: boolean
  ended: string | null
  manifest: 'matched' | 'stale' | 'none' | 'unverified'
  states: string[]
  currentTitle: string | null
  lastFlag: string | null
  stoppedAt?: number | null  // version 12: the step a deliberate stop's answer was tagged with (SPEC-013 BEH-10, BEH-27)
}

/** One enforce refusal with its gate's kind, seq and first flag, kept for the run's review (BEH-25, BEH-26). */
export type ProgressRefused = { gate: string; seq: number; step: number; type: string; message: string }

/** A run paused on the trail (BEH-29, version 14; version 13 held skill, step and tasks only): its return step, its
 *  task IDs, and the values it had while open, which it gets back when the trail unwinds to it (BEH-30). */
export type ProgressPaused = {
  skill: string
  step: number
  tasks: Record<string, number>
  run: ProgressRun | null
  summary: ProgressSummary | null
  marked: boolean
  shown: string[]
  contextSent: number[]
  todos: Record<string, string>
  adhered: string | null
  refusals: Record<string, number>
  refused: ProgressRefused[]
  reviewed: string | null
}

/** A run that ended returned or stopped while nested, kept for the turn's review (BEH-26, BEH-30; version 14). */
export type ProgressReturned = {
  run: ProgressRun | null
  refused: ProgressRefused[]
  reason: 'returned' | 'stopped'
}

/** The documents a Bash call wrote that were recorded, by run ID and path, with their size and mtimeMs as listed (BEH-38 (b); version 22). */
export type ProgressWroteSeen = { [run: string]: { [path: string]: string } }

/** The precompact row's values (SPEC-013 BEH-35, BEH-36, ERR-22; versions 21, 23 and 25): the measured share of the context
 *  window (a whole number from 0 to 100) or null; the row hidden by a load of the precompact skill; BEH-36's run mark (an
 *  automatic run has started since the last compaction); ERR-22's failure; and `pending` (version 26), BEH-36's one-shot mark that
 *  the plugin's own precompact skill is about to load, set in the same write as `ran`, consumed by that load, cleared by ERR-22.
 *  A compaction, /clear, /resume and /branch empty them. */
export type ProgressPrecompact = { percent: number | null; hidden: boolean; ran: boolean; failed: boolean; pending: boolean }

export type ProgressMode = 'observe' | 'enforce'

export type ProgressModeSource = 'framework-default' | 'local'

declare module 'claude-code' {
  interface PluginState {
    devforgeai: {
      run: ProgressRun | null
      mode: ProgressMode
      modeSource: ProgressModeSource
      summary: ProgressSummary | null
      lastEventAt: number
      marked: boolean
      shown: string[]
      contextSent: number[]
      off: string | null
      /** The open run's task IDs and their step numbers (BEH-20); a new run starts empty. */
      tasks: Record<string, number>
      /** TodoWrite: each step's last status, by step number (BEH-20). */
      todos: Record<string, string>
      /** The task-tools hint was shown this session (BEH-23). */
      hinted: boolean
      /** The run given the adherence notice (BEH-22), so a reload doesn't repeat it. */
      adhered: string | null
      /** The open run's enforce refusals by cause, '<gate kind>:<flag type>:<step>' (BEH-25); a new run starts empty. */
      refusals: Record<string, number>
      /** The open run's refusals, each with its gate's kind, seq and first flag, for the review (BEH-25, BEH-26). */
      refused: ProgressRefused[]
      /** The run whose review was asked (BEH-26), so a reload doesn't repeat it. */
      reviewed: string | null
      /** The paused runs, bottom first (BEH-29; version 13, the full values from version 14). */
      trail: ProgressPaused[]
      /** Runs that ended returned or stopped while nested, for the turn's review (BEH-30; version 14). */
      returned: ProgressReturned[]
      /** The runs whose work files cleanup has started, at most once per run (SPEC-013 BEH-32; version 20). */
      cleaned: string[]
      /** For each of the last 20 runs, the documents a Bash call wrote that were recorded, by path, with their size and mtimeMs as
       *  listed, so an unchanged recorded file isn't recorded twice (SPEC-013 BEH-38 (b); version 22). */
      wroteSeen: ProgressWroteSeen
      /** The run whose last evaluation closed BEH-38's window (every step reached, ended, or its validator's step done), or null (version 22). */
      closedFor: string | null
      /** The precompact row's values and BEH-36's run mark (SPEC-013 BEH-35, BEH-36, ERR-22; versions 21, 23 and 25). */
      precompact: ProgressPrecompact
    }
  }
}
