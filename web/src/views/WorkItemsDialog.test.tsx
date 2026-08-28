import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { WorkItemsDialog } from './WorkItemsDialog'
import type { WorkItem } from '../types'

vi.mock('../api', () => ({
  api: { saveWorkItems: vi.fn() },
  ApiError: class ApiError extends Error {
    constructor(public status: number, message: string) { super(message) }
  },
}))
const { api, ApiError } = await import('../api')

/** A promise this test resolves/rejects by hand, so a save can be held open across assertions. */
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

const items: WorkItem[] = [
  { id: 12345, name: 'Sprintarbete', isFavorite: true },
  { id: 12401, name: 'Support', isFavorite: false },
]

beforeEach(() => vi.clearAllMocks())

describe('WorkItemsDialog', () => {
  it('lists the configured work items', () => {
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={vi.fn()} />)

    expect(screen.getByText(/Sprintarbete/)).toBeInTheDocument()
    expect(screen.getByText(/Support/)).toBeInTheDocument()
  })

  it('moves the favourite so exactly one stays favourite', async () => {
    vi.mocked(api.saveWorkItems).mockResolvedValue(undefined)
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: /Gör 12401 till favorit/ }))
    await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

    await waitFor(() => expect(api.saveWorkItems).toHaveBeenCalledWith([
      { id: 12345, name: 'Sprintarbete', isFavorite: false },
      { id: 12401, name: 'Support', isFavorite: true },
    ]))
  })

  it('refuses to remove the last work item', async () => {
    render(<WorkItemsDialog items={[items[0]]} onSaved={vi.fn()} onClose={vi.fn()} />)

    expect(screen.getByRole('button', { name: /Ta bort 12345/ })).toBeDisabled()
  })

  it('moves the favourite to the remaining item when the favourite is removed', async () => {
    vi.mocked(api.saveWorkItems).mockResolvedValue(undefined)
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={vi.fn()} />)

    await userEvent.click(screen.getByRole('button', { name: /Ta bort 12345/ }))
    await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

    await waitFor(() => expect(api.saveWorkItems).toHaveBeenCalledWith([
      { id: 12401, name: 'Support', isFavorite: true },
    ]))
  })

  it('adds a work item', async () => {
    vi.mocked(api.saveWorkItems).mockResolvedValue(undefined)
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={vi.fn()} />)

    await userEvent.type(screen.getByLabelText(/Nytt work item-ID/), '99999')
    await userEvent.type(screen.getByLabelText(/Nytt namn/), 'Möten')
    await userEvent.click(screen.getByRole('button', { name: /Lägg till/ }))
    await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

    await waitFor(() => expect(vi.mocked(api.saveWorkItems).mock.calls[0][0]).toHaveLength(3))
  })

  it('closes on Escape, like any modal dialog', async () => {
    const onClose = vi.fn()
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={onClose} />)

    await userEvent.keyboard('{Escape}')

    expect(onClose).toHaveBeenCalled()
  })

  it('keeps the dialog open while a save is pending, then shows the error on failure', async () => {
    const onClose = vi.fn()
    const pending = deferred<void>()
    vi.mocked(api.saveWorkItems).mockReturnValue(pending.promise)
    render(<WorkItemsDialog items={items} onSaved={vi.fn()} onClose={onClose} />)

    await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

    // The save is now in flight: neither Escape nor Stäng may close the dialog, or a
    // failure that lands afterwards would be silently swallowed with nothing on screen,
    // and a success would land after the user believed they had cancelled.
    await userEvent.keyboard('{Escape}')
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Stäng' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Stäng' }))
    expect(onClose).not.toHaveBeenCalled()

    pending.reject(new ApiError(500, 'Kunde inte spara work items.'))

    expect(await screen.findByText('Kunde inte spara work items.')).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
  })
})
