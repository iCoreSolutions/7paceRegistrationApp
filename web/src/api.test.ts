import { describe, expect, it, vi, afterEach } from 'vitest'
import { api, ApiError } from './api'
import type { WorkItem } from './types'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const noContent = (status = 204) => new Response(null, { status })

afterEach(() => vi.unstubAllGlobals())

describe('api', () => {
  it('sends the client header on a GET request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ configured: false }))
    vi.stubGlobal('fetch', fetchMock)

    await api.config()

    const [, init] = fetchMock.mock.calls[0]
    expect(init.headers['X-Pace-Client']).toBe('1')
  })

  it('PUTs the config body with the method, body and client header a mutating call requires', async () => {
    const fetchMock = vi.fn().mockResolvedValue(noContent())
    vi.stubGlobal('fetch', fetchMock)

    const body = { organization: 'icore', dailyHours: 8, theme: 'System' as const }
    await api.saveConfig(body)

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/config')
    expect(init.method).toBe('PUT')
    expect(init.headers['X-Pace-Client']).toBe('1')
    expect(JSON.parse(init.body)).toEqual(body)
  })

  it('fetches the work item list', async () => {
    const items: WorkItem[] = [{ id: 1, name: 'Feature X', isFavorite: true }]
    const fetchMock = vi.fn().mockResolvedValue(json(items))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api.workItems()).resolves.toEqual(items)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/workitems')
  })

  it('PUTs the work item list with the method, body and client header a mutating call requires', async () => {
    const fetchMock = vi.fn().mockResolvedValue(noContent())
    vi.stubGlobal('fetch', fetchMock)

    const items: WorkItem[] = [{ id: 1, name: 'Feature X', isFavorite: true }]
    await api.saveWorkItems(items)

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/workitems')
    expect(init.method).toBe('PUT')
    expect(init.headers['X-Pace-Client']).toBe('1')
    expect(JSON.parse(init.body)).toEqual(items)
  })

  it('builds the month URL from year and month', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ days: [] }))
    vi.stubGlobal('fetch', fetchMock)

    await api.month(2026, 6)

    expect(fetchMock.mock.calls[0][0]).toBe('/api/month?year=2026&month=6')
  })

  it('POSTs the register body with the method, body and client header a mutating call requires', async () => {
    const response = {
      postedEntries: 0, failedEntries: 0, skippedDays: 0, totalHours: 0, days: [],
    }
    const fetchMock = vi.fn().mockResolvedValue(json(response))
    vi.stubGlobal('fetch', fetchMock)

    const body = { dates: ['2026-06-22'], lines: [{ workItemId: 1, hours: 8 }], simulate: true }
    await api.register(body)

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/register')
    expect(init.method).toBe('POST')
    expect(init.headers['X-Pace-Client']).toBe('1')
    expect(JSON.parse(init.body)).toEqual(body)
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
