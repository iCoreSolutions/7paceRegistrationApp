import { useState } from 'react'
import { api, ApiError } from '../api'
import type { Config, Theme } from '../types'
import { Dialog } from '../components/Dialog'

const THEMES: Theme[] = ['System', 'Light', 'Dark']
const THEME_LABELS: Record<Theme, string> = { System: 'Följ system', Light: 'Ljust', Dark: 'Mörkt' }

interface Props {
  config: Config
  onSaved: (config: Config) => void
  onClose: () => void
}

export function SettingsDialog({ config, onSaved, onClose }: Props) {
  const [organization, setOrganization] = useState(config.organization)
  const [token, setToken] = useState('')
  const [dailyHours, setDailyHours] = useState(config.dailyHours)
  const [theme, setTheme] = useState<Theme>(config.theme)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save() {
    setBusy(true)
    try {
      // An empty token field means "keep the stored one" - the UI can never read it back.
      await api.saveConfig({
        organization,
        token: token.trim() === '' ? null : token.trim(),
        dailyHours,
        theme,
      })
      onSaved({ ...config, organization, dailyHours, theme, hasToken: config.hasToken || token !== '' })
      onClose()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    } finally {
      setBusy(false)
    }
  }

  const field = 'h-9 rounded-md border px-2.5 text-sm'
  const fieldStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <Dialog title="Inställningar" onClose={onClose} busy={busy}>
      <label className="flex flex-col gap-1">
        <span className="text-[13px] font-medium">Organisation</span>
        <input className={field} style={fieldStyle} value={organization} onChange={(e) => setOrganization(e.target.value)} />
      </label>

      <label className="flex flex-col gap-1">
        <span className="text-[13px] font-medium">Ny API-token</span>
        <input type="password" className={field} style={fieldStyle} value={token}
               onChange={(e) => setToken(e.target.value)} />
        <span className="text-[11px]" style={{ color: 'var(--subtle)' }}>
          Lämna tomt för att behålla den sparade tokenen.
        </span>
      </label>

      <label className="flex flex-col gap-1">
        <span className="text-[13px] font-medium">Timmar per dag</span>
        <input type="number" min={1} max={24} step={0.5} className={`${field} w-28`} style={fieldStyle}
               value={dailyHours} onChange={(e) => setDailyHours(Number(e.target.value) || 0)} />
      </label>

      <fieldset className="flex flex-col gap-1">
        <legend className="text-[13px] font-medium">Tema</legend>
        <div className="flex gap-2">
          {THEMES.map((option) => (
            <label key={option} className="flex items-center gap-1.5 text-[13px]">
              <input type="radio" name="theme" checked={theme === option} onChange={() => setTheme(option)} />
              {THEME_LABELS[option]}
            </label>
          ))}
        </div>
      </fieldset>

      {error && <div className="text-xs" style={{ color: 'var(--danger)' }}>{error}</div>}

      <button
        type="button" disabled={busy} onClick={() => void save()}
        className="h-9 rounded-md border text-sm font-semibold disabled:opacity-50"
        style={{ borderColor: 'var(--accent)', background: 'var(--accent)', color: 'var(--accent-fg)' }}
      >
        {busy ? 'Sparar…' : 'Spara'}
      </button>
    </Dialog>
  )
}
