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
const { api } = await import('../api')

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
})
