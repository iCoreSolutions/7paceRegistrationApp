import { describe, expect, it } from 'vitest'
import {
  emptyWorkdays, monthWorkdays, plannedFor, selectionReducer, summarize, weekDates,
} from './selection'
import type { Day, DayStatus, Month } from './types'
import { datesBetween } from './dates'

// The grid starts Monday 2026-06-01, which is ISO week 23, so the week number follows the
// date's offset from that Monday. Deriving it from the day-of-month instead would give
// 2026-07-01..05 the same week 23 as 2026-06-01..07, and weekDates would return both runs.
const GRID_START = Date.UTC(2026, 5, 1)
const isoWeekFor = (date: string) => {
  const utc = Date.UTC(Number(date.slice(0, 4)), Number(date.slice(5, 7)) - 1, Number(date.slice(8, 10)))
  return 23 + Math.floor((utc - GRID_START) / 604800000)
}

const day = (date: string, over: Partial<Day> = {}): Day => ({
  date, expected: 8, logged: 0, remaining: 8, status: 'empty', hitZeroFloor: false,
  isoWeek: isoWeekFor(date), inMonth: date.startsWith('2026-06'),
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

const empty = { selected: [], anchor: null, sequenceBase: null }

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

  it('extends from the cell the sequence began at, unioning into the existing selection', () => {
    // Reproduces the reviewer's exact repro at the reducer level: a drag selects 10..12, the
    // sequence begins back at 10, and one Shift+ArrowLeft (from=10, to=09) must grow the
    // selection to 09..12 rather than replacing it with just 09 and 10.
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-10', '2026-06-11', '2026-06-12'] })
    state = selectionReducer(state, { type: 'extend', from: '2026-06-10', to: '2026-06-09' })

    expect(state.selected).toEqual(['2026-06-09', '2026-06-10', '2026-06-11', '2026-06-12'])
  })

  it('holds the sequence origin fixed across repeated extends, ignoring a later `from`', () => {
    // The first `extend` in a sequence establishes the anchor from `action.from`. A second
    // `extend` in the SAME sequence must keep using that original anchor, not the `from` it
    // is called with this time (which is just the cell that had focus a moment ago).
    let state = selectionReducer(empty, { type: 'extend', from: '2026-06-15', to: '2026-06-16' })
    expect(state.selected).toEqual(['2026-06-15', '2026-06-16'])

    state = selectionReducer(state, { type: 'extend', from: '2026-06-16', to: '2026-06-17' })

    expect(state.selected).toEqual(['2026-06-15', '2026-06-16', '2026-06-17'])
    expect(state.anchor).toBe('2026-06-15')
  })

  it('never shrinks the selection, even when the extended range does not cover everything selected', () => {
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-10', '2026-06-20'] })
    state = selectionReducer(state, { type: 'extend', from: '2026-06-10', to: '2026-06-11' })

    expect(state.selected).toEqual(['2026-06-10', '2026-06-11', '2026-06-20'])
  })

  it('breaks an in-progress sequence on a plain focus move, so a later extend re-anchors', () => {
    let state = selectionReducer(empty, { type: 'extend', from: '2026-06-15', to: '2026-06-16' })
    expect(state.anchor).toBe('2026-06-15')

    state = selectionReducer(state, { type: 'focusMove' })
    expect(state.anchor).toBeNull()
    expect(state.selected).toEqual(['2026-06-15', '2026-06-16']) // focus move never touches selection

    // A fresh sequence now re-anchors at whatever `from` this new extend supplies.
    state = selectionReducer(state, { type: 'extend', from: '2026-06-20', to: '2026-06-21' })
    expect(state.selected).toEqual(['2026-06-15', '2026-06-16', '2026-06-20', '2026-06-21'])
    expect(state.anchor).toBe('2026-06-20')
  })

  it('is a no-op when focusMove has nothing to clear', () => {
    expect(selectionReducer(empty, { type: 'focusMove' })).toEqual(empty)
  })

  it('retracts symmetrically: stepping focus back shrinks the selection to match, dropping the overshoot', () => {
    // Re-reviewer's exact repro: from a single selected day, extend three days forward, then
    // step back two. The union-into-running-selection bug held the overshoot (06-17) selected
    // even after focus returned to 06-16, which would register hours on a day the user backed
    // away from - unacceptable for an app that writes time.
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-15'] })
    state = selectionReducer(state, { type: 'extend', from: '2026-06-15', to: '2026-06-16' })
    state = selectionReducer(state, { type: 'extend', from: '2026-06-16', to: '2026-06-17' })
    expect(state.selected).toEqual(['2026-06-15', '2026-06-16', '2026-06-17'])

    state = selectionReducer(state, { type: 'extend', from: '2026-06-17', to: '2026-06-16' })

    expect(state.selected).toEqual(['2026-06-15', '2026-06-16'])
    expect(state.selected).not.toContain('2026-06-17')
  })

  it('retracts symmetrically in the other direction too: extending left then stepping right drops the overshoot', () => {
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-20'] })
    state = selectionReducer(state, { type: 'extend', from: '2026-06-20', to: '2026-06-19' })
    state = selectionReducer(state, { type: 'extend', from: '2026-06-19', to: '2026-06-18' })
    expect(state.selected).toEqual(['2026-06-18', '2026-06-19', '2026-06-20'])

    state = selectionReducer(state, { type: 'extend', from: '2026-06-18', to: '2026-06-19' })

    expect(state.selected).toEqual(['2026-06-19', '2026-06-20'])
    expect(state.selected).not.toContain('2026-06-18')
  })

  it('retracts back to, but never past, the days selected before the sequence began', () => {
    // A sequence that starts from a pre-existing multi-day selection (e.g. a drag) must be able
    // to retract all the way back to exactly that base, but the base itself must survive no
    // matter how far the retraction goes - it was not part of what this sequence added.
    let state = selectionReducer(empty, { type: 'set', dates: ['2026-06-10', '2026-06-11', '2026-06-12'] })
    state = selectionReducer(state, { type: 'extend', from: '2026-06-12', to: '2026-06-13' })
    state = selectionReducer(state, { type: 'extend', from: '2026-06-13', to: '2026-06-14' })
    expect(state.selected).toEqual(['2026-06-10', '2026-06-11', '2026-06-12', '2026-06-13', '2026-06-14'])

    // Retract all the way back past the extension, to and then through the pre-existing base.
    state = selectionReducer(state, { type: 'extend', from: '2026-06-14', to: '2026-06-12' })
    expect(state.selected).toEqual(['2026-06-10', '2026-06-11', '2026-06-12'])

    state = selectionReducer(state, { type: 'extend', from: '2026-06-12', to: '2026-06-10' })
    expect(state.selected).toEqual(['2026-06-10', '2026-06-11', '2026-06-12']) // the base survives intact
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

    expect(dates).toHaveLength(22)               // June 2026 weekdays: it starts Monday and ends Tuesday
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

    // 22, 23 and 26 are the empty days (8h each = 24), 24 is the partial day (5h).
    expect(summary).toEqual({
      emptyDays: 3, emptyHours: 24, partialDays: 1, partialHours: 5, skippedDays: 1, totalHours: 29,
    })
  })

  it('sums empty/partial hours from each day\'s own remaining, never count * dailyHours', () => {
    // A day shortened by the pre-holiday rule (WorkSchedule) still classifies as Empty until
    // something is logged against it, so its contribution must come from its own `remaining`
    // rather than an assumed uniform daily target - otherwise the empty total overstates and
    // the partial total can go negative.
    const m = month([
      { date: '2026-06-24', status: 'empty', remaining: 5 },
      { date: '2026-06-25', status: 'partial', logged: 6, remaining: 2 },
    ])

    const summary = summarize(m, ['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25'])

    expect(summary).toEqual({
      emptyDays: 3, emptyHours: 21, partialDays: 1, partialHours: 2, skippedDays: 0, totalHours: 23,
    })
  })

  it('leaves non-working days out of the summary entirely', () => {
    const summary = summarize(month(), ['2026-06-06', '2026-06-07'])

    expect(summary).toEqual({
      emptyDays: 0, emptyHours: 0, partialDays: 0, partialHours: 0, skippedDays: 0, totalHours: 0,
    })
  })
})
