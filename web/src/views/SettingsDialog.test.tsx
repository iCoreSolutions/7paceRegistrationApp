import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SettingsDialog } from './SettingsDialog'
import type { Config } from '../types'

vi.mock('../api', () => ({
  api: { saveConfig: vi.fn() },
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

const config: Config = {
  configured: true, organization: 'icore', dailyHours: 8, theme: 'System', hasToken: true,
}

beforeEach(() => vi.clearAllMocks())

describe('SettingsDialog', () => {
  it('saves the updated organization, keeping the stored token when the token field is left blank', async () => {
    vi.mocked(api.saveConfig).mockResolvedValue(undefined)
    const onSaved = vi.fn()
    render(<SettingsDialog config={config} onSaved={onSaved} onClose={vi.fn()} />)

    await userEvent.clear(screen.getByLabelText(/Organisation/))
    await userEvent.type(screen.getByLabelText(/Organisation/), 'icore2')
    await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

    expect(api.saveConfig).toHaveBeenCalledWith(
      expect.objectContaining({ organization: 'icore2', token: null }),
    )
    expect(onSaved).toHaveBeenCalled()
  })

  it(
    'keeps the dialog open while a save is pending, ignoring Escape and disabling Stäng, ' +
    'then shows the error on failure',
    async () => {
      const onClose = vi.fn()
      const pending = deferred<void>()
      vi.mocked(api.saveConfig).mockReturnValue(pending.promise)
      render(<SettingsDialog config={config} onSaved={vi.fn()} onClose={onClose} />)

      await userEvent.click(screen.getByRole('button', { name: /Spara/ }))

      // The save is now in flight: neither Escape nor Stäng may close the dialog, or a
      // failure that lands afterwards would be silently swallowed with nothing on screen,
      // and a success would land after the user believed they had cancelled.
      await userEvent.keyboard('{Escape}')
      expect(onClose).not.toHaveBeenCalled()
      expect(screen.getByRole('button', { name: 'Stäng' })).toBeDisabled()
      await userEvent.click(screen.getByRole('button', { name: 'Stäng' }))
      expect(onClose).not.toHaveBeenCalled()

      pending.reject(new ApiError(500, 'Kunde inte spara inställningar.'))

      expect(await screen.findByText('Kunde inte spara inställningar.')).toBeInTheDocument()
      expect(onClose).not.toHaveBeenCalled()
    },
  )
})
