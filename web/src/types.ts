export type DayStatus = 'nonWorking' | 'empty' | 'partial' | 'complete' | 'over' | 'unknown'
export type LoadState = 'loaded' | 'failed'
export type Theme = 'System' | 'Light' | 'Dark'

export interface Config {
  configured: boolean
  organization: string
  dailyHours: number
  theme: Theme
  hasToken: boolean
}

/** The token is write-only: omit it to keep the stored one. */
export interface ConfigUpdate {
  organization: string
  token?: string | null
  dailyHours: number
  theme: Theme
}

export interface WorkItem {
  id: number
  name: string
  isFavorite: boolean
}

export interface ExistingLog {
  id: string
  hours: number
  workItemId: number
  workItemName: string | null
  comment: string | null
}

export interface Day {
  date: string
  expected: number
  logged: number
  remaining: number
  status: DayStatus
  hitZeroFloor: boolean
  isoWeek: number
  inMonth: boolean
  holidayName: string | null
  existing: ExistingLog[]
}

export interface Totals {
  expected: number
  logged: number
  missing: number
}

export interface Month {
  year: number
  month: number
  from: string
  to: string
  loadState: LoadState
  error: string | null
  holidayWarning: string | null
  fetchedAt: string
  dailyHours: number
  totals: Totals
  days: Day[]
}

export interface FillLine {
  workItemId: number
  hours: number
}

export interface RegisterRequest {
  dates: string[]
  lines: FillLine[]
  simulate: boolean
}

export interface DayResult {
  date: string
  hours: number
  // Always the PLANNED hours, in both real and simulate runs.
  status: 'ok' | 'partial' | 'failed'
  error: string | null
}

export interface RegisterResponse {
  postedEntries: number
  failedEntries: number
  skippedDays: number
  totalHours: number
  days: DayResult[]
}
