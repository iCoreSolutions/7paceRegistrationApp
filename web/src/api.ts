import type {
  Config, ConfigUpdate, Month, RegisterRequest, RegisterResponse, WorkItem,
} from './types'

/** Every failure the UI can render: a server status plus a message worth showing. */
export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message)
    this.name = 'ApiError'
  }
}

// A custom header forces a CORS preflight, and the server configures no CORS policy, so
// only the same-origin SPA can reach the mutating endpoints. See ClientHeaderFilter.
const headers = { 'Content-Type': 'application/json', 'X-Pace-Client': '1' }

async function call<T>(url: string, method = 'GET', body?: unknown): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, 'Appen svarar inte. Kontrollera att 7Pace Desktop körs.')
  }

  if (!response.ok) {
    let message = `Fel ${response.status}`
    try {
      const payload = (await response.json()) as { error?: string }
      if (payload?.error) message = payload.error
    } catch {
      // Non-JSON error body: keep the status-only message.
    }
    throw new ApiError(response.status, message)
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  config: () => call<Config>('/api/config'),
  saveConfig: (body: ConfigUpdate) => call<void>('/api/config', 'PUT', body),
  workItems: () => call<WorkItem[]>('/api/workitems'),
  saveWorkItems: (items: WorkItem[]) => call<void>('/api/workitems', 'PUT', items),
  month: (year: number, month: number) => call<Month>(`/api/month?year=${year}&month=${month}`),
  register: (body: RegisterRequest) => call<RegisterResponse>('/api/register', 'POST', body),
}
