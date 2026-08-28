import { useState } from 'react'
import { api, ApiError } from '../api'

export function SetupWizard({ onDone }: { onDone: () => void }) {
  const [organization, setOrganization] = useState('')
  const [token, setToken] = useState('')
  const [workItemId, setWorkItemId] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const id = Number(workItemId)
  const complete = organization.trim() !== '' && token.trim() !== '' && id > 0 && name.trim() !== ''

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      // Config first: the work item endpoint does not depend on it, but a bad organization
      // should stop the flow before anything is written.
      await api.saveConfig({ organization: organization.trim(), token: token.trim(), dailyHours: 8, theme: 'System' })
      await api.saveWorkItems([{ id, name: name.trim(), isFavorite: true }])
      onDone()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    } finally {
      setBusy(false)
    }
  }

  const field = 'h-9 rounded-md border px-2.5 text-sm'
  const fieldStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <div className="flex h-full items-center justify-center p-6">
      <div
        className="flex w-full max-w-md flex-col gap-4 rounded-xl border p-6"
        style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
      >
        <div className="flex flex-col gap-1">
          <h1 className="text-lg font-semibold">7Pace Desktop</h1>
          <p className="text-[13px] leading-relaxed" style={{ color: 'var(--subtle)' }}>
            Tre saker behövs innan du kan börja: ditt Azure DevOps-konto, en 7Pace API-token
            och minst ett work item att rapportera på.
          </p>
        </div>

        <Field label="Organisation (Azure DevOps-konto)" hint="Bara kontonamnet, t.ex. icore.">
          <input className={field} style={fieldStyle} value={organization}
                 onChange={(e) => setOrganization(e.target.value)} />
        </Field>

        <Field label="API-token" hint="7Pace: Settings > Reporting and API. Sparas i Windows autentiseringshanterare.">
          <input type="password" className={field} style={fieldStyle} value={token}
                 onChange={(e) => setToken(e.target.value)} />
        </Field>

        <div className="grid grid-cols-[7rem_1fr] gap-3">
          <Field label="Work item-ID">
            <input type="number" className={field} style={fieldStyle} value={workItemId}
                   onChange={(e) => setWorkItemId(e.target.value)} />
          </Field>
          <Field label="Namn">
            <input className={field} style={fieldStyle} value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
        </div>

        {error && (
          <div className="rounded-md p-2 text-xs" style={{ background: 'var(--danger-bg)', color: 'var(--danger)' }}>
            {error}
          </div>
        )}

        <button
          type="button" disabled={!complete || busy} onClick={() => void submit()}
          className="h-9.5 rounded-md border text-sm font-semibold disabled:opacity-50"
          style={{ borderColor: 'var(--accent)', background: 'var(--accent)', color: 'var(--accent-fg)' }}
        >
          {busy ? 'Sparar…' : 'Kom igång'}
        </button>
      </div>
    </div>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-[13px] font-medium">{label}</span>
      {children}
      {hint && <span className="text-[11px] leading-snug" style={{ color: 'var(--subtle)' }}>{hint}</span>}
    </label>
  )
}
