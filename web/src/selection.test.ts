import { describe, expect, it } from 'vitest'
import {
  emptyWorkdays, monthWorkdays, plannedFor, selectionReducer, summarize, weekDates,
} from './selection'
import type { Day, DayStatus, Month } from './types'
import { datesBetween } from './dates'

const day = (date: string, over: Partial<Day> = {}): Day => ({
  date, expected: 8, logged: 0, remaining: 8, status: 'empty', hitZeroFloor: false,
  isoWeek: Number(date.slice(8, 10)) <= 7 ? 23 : 24, inMonth: date.startsWith('2026-06'),
  holidayName: null, existing: [], ...over,
})

const nonWorking = (_date: string): Partial<Day> =>
  ({ status: 'nonWorking' as DayStatus, expected: 0, remaining: 0 })

const month = (over: Partial<Day>[] = []): Month => {
  const days = datesBetween('2026-06-01', '2026-07-05').map((d) => day(d))
  // Weekends are non-working.
  for (const d of days) {
    const weekday = new Date(`${d.date}T00:00:00Z`).getUTCDay()
    if (weekday === 0 || weekday === 6) Object.assign(d, nonWorking(d.date))
  }
  for (const patch of over) {
    const target = days.find((d) => d.date === patch.date)
    if (target) Object.assign(target, patch)
  }
  return {
    year: 2026, month: 6, from: '2026-06-01', to: '2026-07-05', loadState: 'loaded',
    error: null, holidayWarning: null, fetchedAt: '2026-06-30T12:00:00Z', dailyHours: 8,
    totals: { expected: 168, logged: 0, missing: 168 }, days,
  }
}

const empty = { selected: [], anchor: null }

describe('selectionReducer', () => {
  it('starts a drag on one day', () => {
    const state = selectionReducer(empty, { type: 'dragStart', date: '2026-06-22' })

    expect(state.selected).toEqual(['2026-06-22'])
    expect(state.anchor).toBe('2026-06-22')
  })

  it('extends a drag to a range, in either direction', () => {
    let state = selectionReducer(empty, { type: 'dragStart', date: '2026-06-24' })
    state = selectionReducer(state, { type: 'dragTo', date: '2026-06-26' })
    expect(state.selected).toEqual(['2026-06-24', '2026-06-25', '2026-06-26'])

    state = selectionReducer(state, { type: 'dragTo', date: '2026-06-22' })
    expect(state.selected).toEqual(['2026-06-22', '2026-06-23', '2026-06-24'])
  })

  it('ignores dragTo when no drag is in progress', () => {
    expect(selectionReducer(empty, { type: 'dragTo', date: '2026-06-22' })).toEqual(empty)
  })

  it('clears the anchor on dragEnd but keeps the selection', () => {
    let state = selectionReducer(empty, { type: 'dragStart', date: '2026-06-22' })
    state = selectionReducer(state, { type: 'dragEnd' })

    expect(state.selected).toEqual(['2026-06-22'])
    expect(state.anchor).toBeNull()
  })

  it('toggles a single day without disturbing the rest', () => {
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-22', '2026-06-23'] })
    state = selectionReducer(state, { type: 'toggle', date: '2026-06-25' })
    expect(state.selected).toEqual(['2026-06-22', '2026-06-23', '2026-06-25'])

    state = selectionReducer(state, { type: 'toggle', date: '2026-06-22' })
    expect(state.selected).toEqual(['2026-06-23', '2026-06-25'])
  })

  it('sorts and de-duplicates a set', () => {
    const state = selectionReducer(empty, {
      type: 'set', dates: ['2026-06-25', '2026-06-22', '2026-06-25'],
    })

    expect(state.selected).toEqual(['2026-06-22', '2026-06-25'])
  })

  it('clears everything', () => {
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-22'] })
    state = selectionReducer(state, { type: 'clear' })

    expect(state).toEqual(empty)
  })
})

describe('bulk selectors', () => {
  it('selects only unfilled workdays of the month', () => {
    const m = month([
      { date: '2026-06-03', status: 'partial', logged: 6, remaining: 2 },
      { date: '2026-06-04', status: 'complete', logged: 8, remaining: 0 },
    ])

    const dates = emptyWorkdays(m)

    expect(dates).not.toContain('2026-06-06')   // Saturday
    expect(dates).not.toContain('2026-06-03')   // partial, not empty
    expect(dates).not.toContain('2026-06-04')   // complete
    expect(dates).not.toContain('2026-07-01')   // outside the month
    expect(dates).toContain('2026-06-01')
  })

  it('selects a whole week, including its weekend cells', () => {
    expect(weekDates(month(), 23)).toEqual(datesBetween('2026-06-01', '2026-06-07'))
  })

  it('selects every workday of the month', () => {
    const dates = monthWorkdays(month())

    expect(dates).toHaveLength(21)               // June 2026 weekdays
    expect(dates).not.toContain('2026-06-07')
  })
})

describe('preview', () => {
  it('plans the shortfall for a day, and nothing for days that need nothing', () => {
    const m = month([
      { date: '2026-06-24', status: 'partial', logged: 3, remaining: 5 },
      { date: '2026-06-25', status: 'complete', logged: 8, remaining: 0 },
      { date: '2026-06-19', ...nonWorking('2026-06-19'), holidayName: 'Midsommarafton' },
    ])

    expect(plannedFor(m, '2026-06-22')).toBe(8)
    expect(plannedFor(m, '2026-06-24')).toBe(5)
    expect(plannedFor(m, '2026-06-25')).toBe(0)
    expect(plannedFor(m, '2026-06-19')).toBe(0)
  })

  it('plans nothing for an unknown day, so a failed fetch cannot cause a top-up', () => {
    const m = month([{ date: '2026-06-22', status: 'unknown', logged: 0, remaining: 8 }])

    expect(plannedFor(m, '2026-06-22')).toBe(0)
  })

  it('summarises a mixed selection the way the server will', () => {
    const m = month([
      { date: '2026-06-24', status: 'partial', logged: 3, remaining: 5 },
      { date: '2026-06-25', status: 'complete', logged: 8, remaining: 0 },
    ])

    const summary = summarize(m, ['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25', '2026-06-26'])

    expect(summary).toEqual({ emptyDays: 3, partialDays: 1, skippedDays: 1, totalHours: 29 })
  })

  it('leaves non-working days out of the summary entirely', () => {
    const summary = summarize(month(), ['2026-06-06', '2026-06-07'])

    expect(summary).toEqual({ emptyDays: 0, partialDays: 0, skippedDays: 0, totalHours: 0 })
  })
})
