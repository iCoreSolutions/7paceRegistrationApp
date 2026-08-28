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
        selected: state.selected.includes(action.date)
          ? state.selected.filter((d) => d !== action.date)
          : normalise([...state.selected, action.date]),
      }
    case 'set':
      return { selected: normalise(action.dates), anchor: null }
    case 'clear':
      return { selected: [], anchor: null }
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
  partialDays: number
  skippedDays: number
  totalHours: number
}

export function summarize(month: Month, selected: string[]): FillSummaryView {
  let emptyDays = 0
  let partialDays = 0
  let skippedDays = 0
  let totalHours = 0

  for (const date of selected) {
    const day = month.days.find((d) => d.date === date)
    if (!day || day.status === 'nonWorking' || day.status === 'unknown') continue

    if (day.remaining <= 0) {
      skippedDays += 1
      continue
    }
    if (day.status === 'empty') emptyDays += 1
    else partialDays += 1
    totalHours += day.remaining
  }

  return { emptyDays, partialDays, skippedDays, totalHours: Math.round(totalHours * 100) / 100 }
}
