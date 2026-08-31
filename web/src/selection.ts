import { datesBetween } from './dates'
import type { Month } from './types'

export interface SelectionState {
  selected: string[]
  anchor: string | null
  // The selection exactly as it stood right before the current Shift+Arrow sequence began.
  // Always null except mid-sequence (i.e. whenever `anchor` is non-null). See `extend` below.
  sequenceBase: string[] | null
}

export type SelectionAction =
  | { type: 'dragStart'; date: string }
  | { type: 'dragTo'; date: string }
  | { type: 'dragEnd' }
  | { type: 'toggle'; date: string }
  | { type: 'set'; dates: string[] }
  | { type: 'clear' }
  | { type: 'extend'; from: string; to: string }
  | { type: 'focusMove' }

const normalise = (dates: string[]) => [...new Set(dates)].sort()

export function selectionReducer(state: SelectionState, action: SelectionAction): SelectionState {
  switch (action.type) {
    case 'dragStart':
      return { selected: [action.date], anchor: action.date, sequenceBase: null }
    case 'dragTo':
      // A dragTo without an anchor means the pointer entered a cell with no drag in progress.
      return state.anchor ? { ...state, selected: datesBetween(state.anchor, action.date) } : state
    case 'dragEnd':
      return { ...state, anchor: null, sequenceBase: null }
    case 'toggle':
      return {
        ...state,
        anchor: null,
        sequenceBase: null,
        selected: state.selected.includes(action.date)
          ? state.selected.filter((d) => d !== action.date)
          : normalise([...state.selected, action.date]),
      }
    case 'set':
      return { selected: normalise(action.dates), anchor: null, sequenceBase: null }
    case 'clear':
      return { selected: [], anchor: null, sequenceBase: null }
    case 'extend': {
      // `anchor` is the keyboard-extension origin: the cell a Shift+Arrow SEQUENCE began from.
      // `sequenceBase` is the selection exactly as it stood right before that sequence began.
      // Both are established once, on the first `extend` of a sequence (i.e. while
      // `state.anchor` is still null) - the origin from `action.from` (the cell that had focus
      // right before this keypress, NOT the sorted `selected` array's first element, which was
      // the original bug), the base from `state.selected` at that moment - and then held FIXED
      // across every subsequent Shift+Arrow press in the sequence. `focusMove` and every other
      // action null both out, so the next sequence re-anchors and re-captures its own base
      // instead of resuming a stale one.
      //
      // Each press recomputes the result as `sequenceBase` union `range(origin, to)` against
      // that FIXED base - never by unioning into the just-previous `selected` - so the range
      // can grow AND shrink symmetrically as focus moves back and forth, while `sequenceBase`
      // guarantees it can never erase a day that was already selected before the sequence
      // started (by drag, ctrl-click, week-click or "Alla tomma dagar"). Unioning into the
      // running selection instead (the first cut at this fix) could grow but never retract:
      // overshooting past a day and stepping back left it selected regardless, and since this
      // app posts hours for whatever ends up selected, that is a real over-registration risk,
      // not just a cosmetic one.
      const origin = state.anchor ?? action.from
      const base = state.anchor !== null ? (state.sequenceBase ?? []) : state.selected
      return { selected: normalise([...base, ...datesBetween(origin, action.to)]), anchor: origin, sequenceBase: base }
    }
    case 'focusMove':
      // A plain (non-shift) arrow move only repositions focus - it never touches `selected` -
      // but it does end any in-progress Shift+Arrow sequence, so a later Shift+Arrow re-anchors
      // (and re-captures its own base) at the new focus rather than resuming the interrupted one.
      return state.anchor ? { ...state, anchor: null, sequenceBase: null } : state
  }
}

/** Unfilled workdays of the displayed month, ignoring the grid's neighbouring-month cells. */
export const emptyWorkdays = (month: Month) =>
  month.days.filter((d) => d.inMonth && d.status === 'empty').map((d) => d.date)

export const monthWorkdays = (month: Month) =>
  month.days.filter((d) => d.inMonth && d.status !== 'nonWorking').map((d) => d.date)

export const weekDates = (month: Month, isoWeek: number) =>
  month.days.filter((d) => d.isoWeek === isoWeek).map((d) => d.date)

/**
 * Hours this day would receive. Only the day total is computed here - how it splits across
 * work items, and how rounding residuals land, is FillPlanner's job on the server.
 */
export function plannedFor(month: Month, date: string): number {
  const day = month.days.find((d) => d.date === date)
  if (!day) return 0
  if (day.status === 'nonWorking' || day.status === 'unknown') return 0
  return day.remaining
}

export interface FillSummaryView {
  emptyDays: number
  // Hours are accumulated from day.remaining, never derived as count * dailyHours: a day before
  // a holiday is shortened by three hours and still classifies as Empty, so multiplying would
  // overstate the empty total and drive the partial total negative.
  emptyHours: number
  partialDays: number
  partialHours: number
  skippedDays: number
  totalHours: number
}

export function summarize(month: Month, selected: string[]): FillSummaryView {
  let emptyDays = 0
  let partialDays = 0
  let skippedDays = 0
  let totalHours = 0

  let emptyHours = 0
  let partialHours = 0

  for (const date of selected) {
    const day = month.days.find((d) => d.date === date)
    if (!day || day.status === 'nonWorking' || day.status === 'unknown') continue

    if (day.remaining <= 0) {
      skippedDays += 1
      continue
    }
    if (day.status === 'empty') { emptyDays += 1; emptyHours += day.remaining }
    else { partialDays += 1; partialHours += day.remaining }
    totalHours += day.remaining
  }

  const round = (n: number) => Math.round(n * 100) / 100
  return {
    emptyDays, emptyHours: round(emptyHours),
    partialDays, partialHours: round(partialHours),
    skippedDays, totalHours: round(totalHours),
  }
}
