import { datesBetween } from './dates'
import type { Month } from './types'

export interface SelectionState {
  selected: string[]
  anchor: string | null
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
      return { selected: [action.date], anchor: action.date }
    case 'dragTo':
      // A dragTo without an anchor means the pointer entered a cell with no drag in progress.
      return state.anchor ? { ...state, selected: datesBetween(state.anchor, action.date) } : state
    case 'dragEnd':
      return { ...state, anchor: null }
    case 'toggle':
      return {
        ...state,
        anchor: null,
        selected: state.selected.includes(action.date)
          ? state.selected.filter((d) => d !== action.date)
          : normalise([...state.selected, action.date]),
      }
    case 'set':
      return { selected: normalise(action.dates), anchor: null }
    case 'clear':
      return { selected: [], anchor: null }
    case 'extend': {
      // `anchor` doubles here as the keyboard-extension origin: the cell a Shift+Arrow
      // SEQUENCE began from. It is established once, from `action.from` (the cell that had
      // focus right before this keypress), the first time a sequence dispatches `extend`
      // (i.e. while `state.anchor` is still null), and then held fixed across repeated
      // Shift+Arrow presses in that sequence rather than being re-derived from the sorted
      // `selected` array each time (that was the bug: `selected[0]` is the lexicographically
      // smallest date, not where the user started extending from). `focusMove` and every
      // other action null the anchor out, so the NEXT sequence re-anchors at the current
      // focus instead of resuming a stale one.
      //
      // The resulting range is UNIONED into the existing selection, never replacing it, so a
      // sequence can only grow the selection — it can't silently shrink days the user already
      // picked by some other means (drag, ctrl-click, week-click, Alla tomma dagar) just
      // because one Shift+Arrow range happens not to cover them.
      const origin = state.anchor ?? action.from
      return { selected: normalise([...state.selected, ...datesBetween(origin, action.to)]), anchor: origin }
    }
    case 'focusMove':
      // A plain (non-shift) arrow move only repositions focus - it never touches `selected` -
      // but it does end any in-progress Shift+Arrow sequence, so a later Shift+Arrow re-anchors
      // at the new focus rather than resuming the interrupted one.
      return state.anchor ? { ...state, anchor: null } : state
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
