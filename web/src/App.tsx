import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from './api'
import type { Config } from './types'
import { useTheme } from './useTheme'
import { MonthView } from './views/MonthView'
import { SetupWizard } from './views/SetupWizard'

export function App() {
  const [config, setConfig] = useState<Config | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setConfig(await api.config())
      setError(null)
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    }
  }, [])

  useEffect(() => { void load() }, [load])
  useTheme(config?.theme ?? 'System')

  if (error) {
    return (
      <div className="flex h-full items-center justify-center p-6 text-center text-sm" style={{ color: 'var(--danger)' }}>
        {error}
      </div>
    )
  }

  if (!config) return <div className="p-6 text-sm" style={{ color: 'var(--subtle)' }}>Laddar…</div>
  if (!config.configured) return <SetupWizard onDone={() => void load()} />

  return <MonthView config={config} onConfigChanged={setConfig} />
}
