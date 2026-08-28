import { describe, expect, it, vi, afterEach } from 'vitest'
import { api, ApiError } from './api'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.unstubAllGlobals())

describe('api', () => {
  it('sends the client header on every request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ configured: false }))
    vi.stubGlobal('fetch', fetchMock)

    await api.config()

    const [, init] = fetchMock.mock.calls[0]
    expect(init.headers['X-Pace-Client']).toBe('1')
  })

  it('builds the month URL from year and month', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ days: [] }))
    vi.stubGlobal('fetch', fetchMock)

    await api.month(2026, 6)

    expect(fetchMock.mock.calls[0][0]).toBe('/api/month?year=2026&month=6')
  })

  it('throws ApiError carrying the status and the server message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ error: 'Ogiltig månad.' }, 400)))

    await expect(api.month(2026, 13)).rejects.toMatchObject({
      status: 400,
      message: 'Ogiltig månad.',
    })
    await expect(api.month(2026, 13)).rejects.toBeInstanceOf(ApiError)
  })

  it('reports a conflict from register distinctly, so the UI can say nothing was posted', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ error: 'Kunde inte hämta.' }, 409)))

    await expect(api.register({ dates: ['2026-06-22'], lines: [], simulate: false }))
      .rejects.toMatchObject({ status: 409 })
  })

  it('surfaces an unreachable server as a clear message rather than a parse error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    await expect(api.config()).rejects.toMatchObject({ status: 0 })
  })
})
