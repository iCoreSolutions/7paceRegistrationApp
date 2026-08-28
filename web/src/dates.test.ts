import { describe, expect, it } from 'vitest'
import { addMonths, datesBetween, formatMonth, weekRows } from './dates'
import type { Day } from './types'

const day = (date: string): Day => ({
  date, expected: 8, logged: 0, remaining: 8, status: 'empty', hitZeroFloor: false,
  isoWeek: 1, inMonth: true, holidayName: null, existing: [],
})

describe('dates', () => {
  it('formats a month in Swedish', () => {
    expect(formatMonth(2026, 6)).toBe('Juni 2026')
    expect(formatMonth(2026, 12)).toBe('December 2026')
  })

  it('steps months across a year boundary', () => {
    expect(addMonths(2026, 12, 1)).toEqual({ year: 2027, month: 1 })
    expect(addMonths(2026, 1, -1)).toEqual({ year: 2025, month: 12 })
  })

  it('lists the dates between two days inclusively, in either order', () => {
    expect(datesBetween('2026-06-22', '2026-06-25'))
      .toEqual(['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25'])
    expect(datesBetween('2026-06-25', '2026-06-22'))
      .toEqual(['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25'])
    expect(datesBetween('2026-06-22', '2026-06-22')).toEqual(['2026-06-22'])
  })

  it('splits a 35-day grid into five rows of seven', () => {
    const days = datesBetween('2026-06-01', '2026-07-05').map(day)

    const rows = weekRows(days)

    expect(rows).toHaveLength(5)
    expect(rows.every((r) => r.length === 7)).toBe(true)
    expect(rows[0][0].date).toBe('2026-06-01')
  })
})
