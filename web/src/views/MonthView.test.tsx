import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MonthView } from './MonthView'
import type { Config, Month } from '../types'
import { addMonths, datesBetween } from '../dates'

const config: Config = {
  configured: true, organization: 'icore', dailyHours: 8, theme: 'System', hasToken: true,
}

const renderMonthView = () => render(<MonthView config={config} onConfigChanged={vi.fn()} />)

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

/**
 * Wires api.month to resolve the way the real server does: echoing back exactly the
 * (year, month) it was asked for (see MonthEndpoints.cs, which builds MonthDto from the
 * request's own query parameters, unconditionally). Use this — never a flat
 * mockResolvedValue(monthPayload()) — for any test that asserts which (year, month) a
 * call used, or that exercises more than one load: a mock that ignores its arguments can
 * silently hide a bug in what the component actually requested.
 */
const mockMonthEcho = (over: Partial<Month> = {}) =>
  vi.mocked(api.month).mockImplementation(async (year, month) => monthPayload({ year, month, ...over }))

describe('MonthView', () => {
  it('renders the fetched month, its totals and its title', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    renderMonthView()

    // The month name legitimately appears twice: in the header title and in the status bar.
    expect((await screen.findAllByText('Juni 2026')).length).toBeGreaterThan(0)
    expect(screen.getByText(/av 168 h loggade/)).toBeInTheDocument()
    expect(screen.getByText(/85 h saknas/)).toBeInTheDocument()
  })

  it('renders one cell per grid day, including the neighbouring month', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    renderMonthView()

    await waitFor(() => expect(screen.getAllByRole('button', { name: /2026-/ })).toHaveLength(35))
  })

  it('shows the week-number gutter', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())

    renderMonthView()

    expect(await screen.findByRole('button', { name: /vecka 23/i })).toBeInTheDocument()
  })

  it('steps to the next month and refetches', async () => {
    // Echoing (not a fixed payload) so the initial mount — which starts from today's
    // wall-clock month, whatever that is — has nothing to reconcile, and the assertion
    // below verifies actual accumulation rather than coincidentally matching a hardcoded
    // "next" value.
    mockMonthEcho()
    renderMonthView()
    await waitFor(() => expect(api.month).toHaveBeenCalledTimes(1))
    const [startYear, startMonth] = vi.mocked(api.month).mock.calls[0]

    await userEvent.click(screen.getByRole('button', { name: 'Nästa månad' }))

    const expected = addMonths(startYear, startMonth, 1)
    await waitFor(() => expect(api.month).toHaveBeenLastCalledWith(expected.year, expected.month))
  })

  it('accumulates rapid clicks on Nästa månad instead of retargeting the same month, firing exactly two requests', async () => {
    // Echoing back exactly the year/month it was asked for, like the real server does, so
    // the initial mount (which starts from today's wall-clock month, whatever that is)
    // settles with nothing to reconcile — this test does not depend on today's date or on
    // any particular starting month.
    mockMonthEcho()
    renderMonthView()

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

    // waitFor returns the instant the count first reaches 2 — that alone doesn't prove no
    // further call is on its way on a later tick. Give one a real chance to arrive, then
    // assert the count is still exactly 2 and the two calls are exactly what's expected.
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(api.month).toHaveBeenCalledTimes(2)
    expect(vi.mocked(api.month).mock.calls).toEqual([
      [expectedFirst.year, expectedFirst.month],
      [expectedSecond.year, expectedSecond.month],
    ])
  })

  it('warns when the holiday list could not be fetched', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload({ holidayWarning: 'Kunde inte hämta röda dagar.' }))

    renderMonthView()

    expect(await screen.findByText(/Kunde inte hämta röda dagar/)).toBeInTheDocument()
  })

  it('shows the fetch failure without pretending the days are empty', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload({
      loadState: 'failed',
      error: '7Pace API error 401: nope',
      days: monthPayload().days.map((d) => ({ ...d, status: 'unknown' as const })),
    }))

    renderMonthView()

    expect(await screen.findByText(/kunde inte hämtas/i)).toBeInTheDocument()
    expect(screen.getAllByText('ej hämtad').length).toBeGreaterThan(0)
  })

  it('selects a range by dragging across cells', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    const from = screen.getByRole('button', { name: /2026-06-22/ })
    const to = screen.getByRole('button', { name: /2026-06-24/ })
    await userEvent.pointer([
      { keys: '[MouseLeft>]', target: from },
      { target: to },
      { keys: '[/MouseLeft]' },
    ])

    expect(screen.getByRole('button', { name: /2026-06-23/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-25/ })).toHaveAttribute('aria-pressed', 'false')
  })

  it('leaves a built-up selection intact when a non-primary button presses a cell', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    const from = screen.getByRole('button', { name: /2026-06-22/ })
    const to = screen.getByRole('button', { name: /2026-06-24/ })
    await userEvent.pointer([
      { keys: '[MouseLeft>]', target: from },
      { target: to },
      { keys: '[/MouseLeft]' },
    ])
    expect(screen.getByRole('button', { name: /2026-06-22/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-24/ })).toHaveAttribute('aria-pressed', 'true')

    // A stray right-click (e.g. reaching for a future context menu) must not collapse the
    // multi-day selection built up above down to just the clicked cell.
    await userEvent.pointer([{ keys: '[MouseRight]', target: to }])

    expect(screen.getByRole('button', { name: /2026-06-22/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-23/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-24/ })).toHaveAttribute('aria-pressed', 'true')
  })

  it('toggles a single day with ctrl-click without clearing the rest', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    // A shared session is required here: the top-level userEvent.* calls each start their own
    // fresh input-device state, so a Control key held via a bare `userEvent.keyboard('{Control>}')`
    // would never reach a later independent `userEvent.click(...)` call.
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /2026-06-22/ }))
    await user.keyboard('{Control>}')
    await user.click(screen.getByRole('button', { name: /2026-06-25/ }))
    await user.keyboard('{/Control}')

    expect(screen.getByRole('button', { name: /2026-06-22/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-25/ })).toHaveAttribute('aria-pressed', 'true')
  })

  it('selects a whole week from the gutter', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: /vecka 23/i }))

    expect(screen.getByRole('button', { name: /2026-06-01/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-07/ })).toHaveAttribute('aria-pressed', 'true')

    // Keyboard, not just mouse: a focused native <button> triggers a click on Enter via the
    // browser's own semantics, which is why this button needs no bespoke keydown handler.
    screen.getByRole('button', { name: /vecka 24/i }).focus()
    await userEvent.keyboard('{Enter}')

    expect(screen.getByRole('button', { name: /2026-06-08/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /2026-06-14/ })).toHaveAttribute('aria-pressed', 'true')
  })

  it('selects every unfilled workday, then clears', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: 'Alla tomma dagar' }))
    expect(screen.getByRole('button', { name: /2026-06-01/ })).toHaveAttribute('aria-pressed', 'true')

    await userEvent.click(screen.getByRole('button', { name: 'Rensa markering' }))
    expect(screen.getByRole('button', { name: /2026-06-01/ })).toHaveAttribute('aria-pressed', 'false')
  })

  it('toggles the focused day with the space key', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    const cell = screen.getByRole('button', { name: /2026-06-22/ })
    cell.focus()
    await userEvent.keyboard(' ')

    expect(cell).toHaveAttribute('aria-pressed', 'true')
  })

  it('badges the planned top-up on selected days', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: /2026-06-22/ }))

    expect(screen.getByText('+8 h')).toBeInTheDocument()
  })

  it('opens and closes the settings dialog from the gear button, by mouse and by keyboard', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: 'Inställningar' }))
    expect(screen.getByRole('dialog', { name: 'Inställningar' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Stäng' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    // A focusable, real <button> triggers its click on Enter via the browser's own semantics
    // (see the week-gutter test above), so opening it via keyboard needs no bespoke handler.
    screen.getByRole('button', { name: 'Inställningar' }).focus()
    await userEvent.keyboard('{Enter}')
    expect(screen.getByRole('dialog', { name: 'Inställningar' })).toBeInTheDocument()
  })

  it('opens the work items dialog from the toolbar button', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    renderMonthView()
    await screen.findAllByText('Juni 2026')

    screen.getByRole('button', { name: 'Work items' }).focus()
    await userEvent.keyboard('{Enter}')

    expect(screen.getByRole('dialog', { name: 'Work items' })).toBeInTheDocument()
  })

  it('cycles the theme with the moon button and reports the change upward', async () => {
    vi.mocked(api.month).mockResolvedValue(monthPayload())
    const onConfigChanged = vi.fn()
    render(<MonthView config={config} onConfigChanged={onConfigChanged} />)
    await screen.findAllByText('Juni 2026')

    await userEvent.click(screen.getByRole('button', { name: 'Tema: System' }))

    expect(onConfigChanged).toHaveBeenCalledWith({ ...config, theme: 'Light' })
  })
})
