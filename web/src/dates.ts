import type { Day } from './types'

const MONTHS = [
  'Januari', 'Februari', 'Mars', 'April', 'Maj', 'Juni',
  'Juli', 'Augusti', 'September', 'Oktober', 'November', 'December',
]

export const WEEKDAYS = ['mån', 'tis', 'ons', 'tor', 'fre', 'lör', 'sön']

export const formatMonth = (year: number, month: number) => `${MONTHS[month - 1]} ${year}`

export function addMonths(year: number, month: number, delta: number) {
  const zeroBased = year * 12 + (month - 1) + delta
  return { year: Math.floor(zeroBased / 12), month: (zeroBased % 12) + 1 }
}

/** Dates are handled as plain ISO strings; UTC arithmetic keeps them free of timezone drift. */
export function datesBetween(a: string, b: string): string[] {
  const [from, to] = a <= b ? [a, b] : [b, a]
  const out: string[] = []
  for (let d = new Date(`${from}T00:00:00Z`); ; d = new Date(d.getTime() + 86400000)) {
    const iso = d.toISOString().slice(0, 10)
    out.push(iso)
    if (iso >= to) break
  }
  return out
}

export function weekRows(days: Day[]): Day[][] {
  const rows: Day[][] = []
  for (let i = 0; i < days.length; i += 7) rows.push(days.slice(i, i + 7))
  return rows
}

/** Swedish decimal formatting, trimming a trailing ",0". */
export const hours = (value: number) =>
  Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/\.?0+$/, '').replace('.', ',')
