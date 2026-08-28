import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MonthView } from './MonthView'
import type { Month } from '../types'
import { addMonths, datesBetween } from '../dates'

const monthPayload = (over: Partial<Month> = {}): Month => ({
  year: 2026, month: 6, from: '2026-06-01', to: '2026-07-05',
  loadState: 'loaded', error: null, holidayWarning: null,
  fetchedAt: '2026-06-30T12:00:00Z', dailyHours: 8,
  totals: { expected: 168, logged: 83, missing: 85 },
  // 2026-06-01 is a Monday (ISO week 23), and this range is exactly five whole
  // Monday-first weeks, so each row of 7 carries the next sequential ISO week number.
  days: datesBetween('2026-06-01', '2026-07-05').map((date, index) => ({
    date, expected: 8, logged: 0, remaining: 8, status: 'empty' as const,
    hitZeroFloor: false, isoWeek: 23 + Math.floor(index / 7), inMonth: date.startsWith('2026-06'),
    holidayName: null, existing: [],
  })),
  ...over,
})

vi.mock('../api', () => ({
  api: { month: vi.fn(), register: vi.fn(), workItems: vi.fn().mockResolvedValue([]) },
  ApiError: class extends Error {},
}))

const { api } = await import('../api')

beforeEach(() => vi.clearAllMocks())

/** A promise this test resolves by hand, so a fetch can be held open across two clicks. */
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

describe('MonthView', () => {
  it('renders the fetched month, its totals and its title', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    render(<MonthView />)

    // The month name legitimately appears twice: in the header title and in the status bar.
    expect((await screen.findAllByText('Juni 2026')).length).toBeGreaterThan(0)
    expect(screen.getByText(/av 168 h loggade/)).toBeInTheDocument()
    expect(screen.getByText(/85 h saknas/)).toBeInTheDocument()
  })

  it('renders one cell per grid day, including the neighbouring month', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    render(<MonthView />)

    await waitFor(() => expect(screen.getAllByRole('button', { name: /2026-/ })).toHaveLength(35))
  })

  it('shows the week-number gutter', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    render(<MonthView />)

    expect(await screen.findByRole('button', { name: /vecka 23/i })).toBeInTheDocument()
  })

  it('steps to the next month and refetches', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    render(<MonthView />)
    await screen.findAllByText('Juni 2026')

    vi.mocked(api.month).mockResolvedValue(monthPayload({ year: 2026, month: 7 }))
    await userEvent.click(screen.getByRole('button', { name: 'Nästa månad' }))

    await waitFor(() => expect(api.month).toHaveBeenLastCalledWith(2026, 7))
  })

  it('accumulates rapid clicks on Nästa månad instead of retargeting the same month', async () => {
    // The mock echoes back exactly the year/month it was asked for, like the real server
    // does, so the initial mount (which starts from today's wall-clock month, whatever
    // that is) settles with nothing to reconcile — this test does not depend on today's
    // date or on any particular starting month.
    vi.mocked(api.month).mockImplementation(async (year, month) => monthPayload({ year, month }))
    render(<MonthView />)

    await waitFor(() => expect(api.month).toHaveBeenCalledTimes(1))
    const [startYear, startMonth] = vi.mocked(api.month).mock.calls[0]
    const next = await screen.findByRole('button', { name: 'Nästa månad' })

    const firstClick = deferred<Month>()
    const secondClick = deferred<Month>()
    vi.mocked(api.month).mockReset()
    vi.mocked(api.month)
      .mockReturnValueOnce(firstClick.promise)
      .mockReturnValueOnce(secondClick.promise)

    await userEvent.click(next)
    await userEvent.click(next)

    // Both clicks fire before either fetch resolves. They must target two DIFFERENT,
    // consecutive months rather than the same "next month" request twice.
    const expectedFirst = addMonths(startYear, startMonth, 1)
    const expectedSecond = addMonths(startYear, startMonth, 2)
    expect(api.month).toHaveBeenNthCalledWith(1, expectedFirst.year, expectedFirst.month)
    expect(api.month).toHaveBeenNthCalledWith(2, expectedSecond.year, expectedSecond.month)

    firstClick.resolve(monthPayload(expectedFirst))
    secondClick.resolve(monthPayload(expectedSecond))
    await waitFor(() => expect(api.month).toHaveBeenCalledTimes(2))
  })

  it('warns when the holiday list could not be fetched', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload({ holidayWarning: 'Kunde inte hämta röda dagar.' }))

    render(<MonthView />)

    expect(await screen.findByText(/Kunde inte hämta röda dagar/)).toBeInTheDocument()
  })

  it('shows the fetch failure without pretending the days are empty', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload({
      loadState: 'failed',
      error: '7Pace API error 401: nope',
      days: monthPayload().days.map((d) => ({ ...d, status: 'unknown' as const })),
    }))

    render(<MonthView />)

    expect(await screen.findByText(/kunde inte hämtas/i)).toBeInTheDocument()
    expect(screen.getAllByText('ej hämtad').length).toBeGreaterThan(0)
  })
})
