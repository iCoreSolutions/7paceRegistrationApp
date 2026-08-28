import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SetupWizard } from './SetupWizard'

vi.mock('../api', () => ({
  api: { saveConfig: vi.fn(), saveWorkItems: vi.fn() },
  ApiError: class ApiError extends Error {
    constructor(public status: number, message: string) { super(message) }
  },
}))
const { api } = await import('../api')

beforeEach(() => vi.clearAllMocks())

describe('SetupWizard', () => {
  it('cannot be completed until organization, token and a work item are given', async () => {
    render(<SetupWizard onDone={vi.fn()} />)
    const done = screen.getByRole('button', { name: /Kom igång/ })
    expect(done).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/Organisation/), 'icore')
    expect(done).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/API-token/), 'secret')
    expect(done).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/Work item-ID/), '12345')
    await userEvent.type(screen.getByLabelText(/Namn/), 'Sprintarbete')
    expect(done).toBeEnabled()
  })

  it('saves the config and the first work item, marking it favourite', async () => {
    const onDone = vi.fn()
    vi.mocked(api.saveConfig).mockResolvedValue(undefined)
    vi.mocked(api.saveWorkItems).mockResolvedValue(undefined)
    render(<SetupWizard onDone={onDone} />)

    await userEvent.type(screen.getByLabelText(/Organisation/), 'icore')
    await userEvent.type(screen.getByLabelText(/API-token/), 'secret')
    await userEvent.type(screen.getByLabelText(/Work item-ID/), '12345')
    await userEvent.type(screen.getByLabelText(/Namn/), 'Sprintarbete')
    await userEvent.click(screen.getByRole('button', { name: /Kom igång/ }))

    await waitFor(() => expect(api.saveConfig).toHaveBeenCalledWith(
      expect.objectContaining({ organization: 'icore', token: 'secret' }),
    ))
    expect(api.saveWorkItems).toHaveBeenCalledWith([
      { id: 12345, name: 'Sprintarbete', isFavorite: true },
    ])
    expect(onDone).toHaveBeenCalled()
  })

  it('shows the server message when the organization is rejected', async () => {
    const { ApiError } = await import('../api')
    vi.mocked(api.saveConfig).mockRejectedValue(new ApiError(400, "'iCore v3' är inte ett giltigt kontonamn."))
    render(<SetupWizard onDone={vi.fn()} />)

    await userEvent.type(screen.getByLabelText(/Organisation/), 'iCore v3')
    await userEvent.type(screen.getByLabelText(/API-token/), 'secret')
    await userEvent.type(screen.getByLabelText(/Work item-ID/), '1')
    await userEvent.type(screen.getByLabelText(/Namn/), 'A')
    await userEvent.click(screen.getByRole('button', { name: /Kom igång/ }))

    expect(await screen.findByText(/inte ett giltigt kontonamn/)).toBeInTheDocument()
  })

  it('does not echo the token back into the DOM as plain text', async () => {
    render(<SetupWizard onDone={vi.fn()} />)

    const token = screen.getByLabelText(/API-token/)

    expect(token).toHaveAttribute('type', 'password')
  })
})
