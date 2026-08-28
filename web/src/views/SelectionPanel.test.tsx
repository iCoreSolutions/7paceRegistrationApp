import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SelectionPanel } from './SelectionPanel'
import type { Month, WorkItem } from '../types'
import { datesBetween } from '../dates'

vi.mock('../api', () => ({
  api: { register: vi.fn() },
  ApiError: class ApiError extends Error {
    constructor(public status: number, message: string) { super(message) }
  },
}))
const { api } = await import('../api')

const workItems: WorkItem[] = [
  { id: 12345, name: 'Sprintarbete', isFavorite: true },
  { id: 12401, name: 'Support', isFavorite: false },
]

const month = (loadState: 'loaded' | 'failed' = 'loaded'): Month => ({
  year: 2026, month: 6, from: '2026-06-01', to: '2026-07-05', loadState,
  error: loadState === 'failed' ? 'nope' : null, holidayWarning: null,
  fetchedAt: '2026-06-30T12:00:00Z', dailyHours: 8,
  totals: { expected: 168, logged: 0, missing: 168 },
  days: datesBetween('2026-06-01', '2026-07-05').map((date) => ({
    date, expected: 8, logged: 0, remaining: 8,
    status: loadState === 'failed' ? ('unknown' as const) : ('empty' as const),
    hitZeroFloor: false, isoWeek: 26, inMonth: true, holidayName: null, existing: [],
  })),
})

const panel = (over: Partial<Parameters<typeof SelectionPanel>[0]> = {}) =>
  render(
    <SelectionPanel
      month={month()}
      workItems={workItems}
      selected={['2026-06-22', '2026-06-23']}
      onRegistered={vi.fn()}
      onClear={vi.fn()}
      {...over}
    />,
  )

beforeEach(() => vi.clearAllMocks())

describe('SelectionPanel', () => {
  it('reports the selected day count and the hours to register', () => {
    panel()

    expect(screen.getByText('2 dagar valda')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registrera 16 h/ })).toBeEnabled()
  })

  it('seeds one line on the favourite work item at the full daily target', () => {
    panel()

    expect(screen.getByRole('combobox')).toHaveValue('12345')
    expect(screen.getByLabelText(/timmar för rad 1/i)).toHaveValue(8)
  })

  it('blocks registering until the lines sum to the daily target', async () => {
    panel()

    await userEvent.click(screen.getByRole('button', { name: /Lägg till work item/ }))

    // The new line starts at 0 h, so the split now sums to 8 of 8 and stays balanced.
    await userEvent.clear(screen.getByLabelText(/timmar för rad 1/i))
    await userEvent.type(screen.getByLabelText(/timmar för rad 1/i), '5')

    expect(screen.getByText(/5 av 8 h/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registrera/ })).toBeDisabled()
  })

  it('summarises empty, partial and skipped days', () => {
    const m = month()
    m.days.find((d) => d.date === '2026-06-24')!.logged = 3
    m.days.find((d) => d.date === '2026-06-24')!.remaining = 5
    m.days.find((d) => d.date === '2026-06-24')!.status = 'partial'
    m.days.find((d) => d.date === '2026-06-25')!.logged = 8
    m.days.find((d) => d.date === '2026-06-25')!.remaining = 0
    m.days.find((d) => d.date === '2026-06-25')!.status = 'complete'

    panel({ month: m, selected: ['2026-06-22', '2026-06-23', '2026-06-24', '2026-06-25', '2026-06-26'] })

    expect(screen.getByText(/3 tomma dagar/)).toBeInTheDocument()
    expect(screen.getByText(/1 delvis dag/)).toBeInTheDocument()
    expect(screen.getByText(/hoppas över/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Registrera 29 h/ })).toBeInTheDocument()
  })

  it('posts the selection and the lines, then reports back', async () => {
    const onRegistered = vi.fn()
    vi.mocked(api.register).mockResolvedValue({
      postedEntries: 2, failedEntries: 0, skippedDays: 0, totalHours: 16,
      days: [
        { date: '2026-06-22', hours: 8, status: 'ok', error: null },
        { date: '2026-06-23', hours: 8, status: 'ok', error: null },
      ],
    })
    panel({ onRegistered })

    await userEvent.click(screen.getByRole('button', { name: /Registrera 16 h/ }))

    await waitFor(() => expect(api.register).toHaveBeenCalledWith({
      dates: ['2026-06-22', '2026-06-23'],
      lines: [{ workItemId: 12345, hours: 8 }],
      simulate: false,
    }))
    expect(onRegistered).toHaveBeenCalled()
  })

  it('passes the simulate flag when the box is ticked', async () => {
    vi.mocked(api.register).mockResolvedValue({
      postedEntries: 2, failedEntries: 0, skippedDays: 0, totalHours: 16, days: [],
    })
    panel()

    await userEvent.click(screen.getByLabelText(/Simulera/))
    await userEvent.click(screen.getByRole('button', { name: /Registrera/ }))

    await waitFor(() => expect(vi.mocked(api.register).mock.calls[0][0].simulate).toBe(true))
  })

  it('shows per-day failures after a partial success', async () => {
    vi.mocked(api.register).mockResolvedValue({
      postedEntries: 1, failedEntries: 1, skippedDays: 0, totalHours: 16,
      days: [
        { date: '2026-06-22', hours: 8, status: 'ok', error: null },
        { date: '2026-06-23', hours: 8, status: 'failed', error: '7Pace API error 500: boom' },
      ],
    })
    panel()

    await userEvent.click(screen.getByRole('button', { name: /Registrera/ }))

    expect(await screen.findByText(/2026-06-23/)).toBeInTheDocument()
    expect(screen.getByText(/500/)).toBeInTheDocument()
  })

  it('says nothing was registered when the server refuses on a stale read', async () => {
    const { ApiError } = await import('../api')
    vi.mocked(api.register).mockRejectedValue(new ApiError(409, 'Kunde inte hämta redan registrerad tid.'))
    panel()

    await userEvent.click(screen.getByRole('button', { name: /Registrera/ }))

    expect(await screen.findByText(/Kunde inte hämta redan registrerad tid/)).toBeInTheDocument()
  })

  it('blocks registering entirely when the month could not be fetched', () => {
    panel({ month: month('failed') })

    expect(screen.getByRole('button', { name: /Registrera/ })).toBeDisabled()
    expect(screen.getByText(/kunde inte hämtas/i)).toBeInTheDocument()
    expect(screen.getByText('— h')).toBeInTheDocument()
  })

  it('prompts to select days when nothing is selected', () => {
    panel({ selected: [] })

    expect(screen.getByText(/Dra i kalendern/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Registrera/ })).toBeDisabled()
  })
})
