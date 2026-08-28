import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MonthView } from './MonthView'
import type { Month } from '../types'
import { datesBetween } from '../dates'

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
