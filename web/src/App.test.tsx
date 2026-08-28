import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

vi.mock('./api', () => ({
  api: { config: vi.fn(), month: vi.fn().mockResolvedValue(null), workItems: vi.fn().mockResolvedValue([]) },
  ApiError: class ApiError extends Error {
    constructor(public status: number, message: string) { super(message) }
  },
}))
const { api } = await import('./api')

beforeEach(() => vi.clearAllMocks())

describe('App', () => {
  it('shows the setup wizard until the app is configured', async () => {
    vi.mocked(api.config).mockResolvedValue({
      configured: false, organization: '', dailyHours: 8, theme: 'System', hasToken: false,
    })

    render(<App />)

    expect(await screen.findByText(/Kom igång/)).toBeInTheDocument()
  })

  it('shows the calendar once configured', async () => {
    vi.mocked(api.config).mockResolvedValue({
      configured: true, organization: 'icore', dailyHours: 8, theme: 'System', hasToken: true,
    })

    render(<App />)

    expect(await screen.findByText('7Pace Desktop')).toBeInTheDocument()
  })

  it('says the app is not running when the API cannot be reached', async () => {
    const { ApiError } = await import('./api')
    vi.mocked(api.config).mockRejectedValue(new ApiError(0, 'Appen svarar inte.'))

    render(<App />)

    expect(await screen.findByText(/svarar inte/)).toBeInTheDocument()
  })
})
