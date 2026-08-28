import { useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import type { FillLine, Month, RegisterResponse, WorkItem } from '../types'
import { hours } from '../dates'
import { summarize } from '../selection'
import { Check, Close, Plus, Warning } from '../components/Icons'

interface Props {
  month: Month
  workItems: WorkItem[]
  selected: string[]
  onRegistered: (response: RegisterResponse) => void
  onClear: () => void
}

const EPSILON = 0.001

export function SelectionPanel({ month, workItems, selected, onRegistered, onClear }: Props) {
  const favourite = workItems.find((w) => w.isFavorite) ?? workItems[0]
  const [lines, setLines] = useState<FillLine[]>([])
  const [simulate, setSimulate] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<RegisterResponse | null>(null)
  // Captured at submit time, not read live from `simulate`: the checkbox may change again
  // before the user looks at this result, but the message must describe what actually ran.
  const [resultSimulated, setResultSimulated] = useState(false)

  // One line on the favourite at the full target is the common case, so it is the default.
  useEffect(() => {
    if (favourite) setLines([{ workItemId: favourite.id, hours: month.dailyHours }])
  }, [favourite, month.dailyHours])

  const summary = useMemo(() => summarize(month, selected), [month, selected])
  const linesSum = lines.reduce((total, line) => total + line.hours, 0)
  const balanced = Math.abs(linesSum - month.dailyHours) <= EPSILON
  const blocked = month.loadState === 'failed'
  const canRegister = !blocked && !busy && balanced && selected.length > 0 && summary.totalHours > 0

  const update = (index: number, patch: Partial<FillLine>) =>
    setLines((current) => current.map((line, i) => (i === index ? { ...line, ...patch } : line)))

  async function register() {
    setBusy(true)
    setError(null)
    setResult(null)
    const usedSimulate = simulate
    try {
      const response = await api.register({ dates: selected, lines, simulate: usedSimulate })
      setResult(response)
      setResultSimulated(usedSimulate)
      onRegistered(response)
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    } finally {
      setBusy(false)
    }
  }

  const label = 'text-[11px] font-semibold uppercase tracking-wide'
  const field = 'h-8 rounded-md border px-2 text-[13px]'
  const fieldStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <aside
      className="flex w-85 shrink-0 flex-col gap-3.5 overflow-y-auto border-l p-4"
      style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
    >
      <div className="flex flex-col gap-1">
        <span className="text-[15px] font-semibold">
          {selected.length === 0
            ? 'Inga dagar valda'
            : `${selected.length} ${selected.length === 1 ? 'dag' : 'dagar'} valda`}
        </span>
        <span className="text-xs" style={{ color: 'var(--subtle)' }}>
          {selected.length === 0
            ? 'Dra i kalendern för att välja dagar.'
            : `${selected[0]} – ${selected[selected.length - 1]}`}
        </span>
      </div>

      {blocked && (
        <div
          className="flex gap-2.5 rounded-lg border p-3"
          style={{ borderColor: 'var(--danger)', background: 'var(--danger-bg)' }}
        >
          <span className="shrink-0" style={{ color: 'var(--danger)' }}><Warning /></span>
          <div className="flex flex-col gap-1">
            <span className="text-[13px] font-semibold">Registrerad tid kunde inte hämtas</span>
            <span className="text-xs leading-relaxed" style={{ color: 'var(--subtle)' }}>
              Appen vet inte vad som redan är loggat och skulle riskera att dubbelregistrera.
              Uppdatera för att försöka igen.
            </span>
          </div>
        </div>
      )}

      <div className="flex flex-col gap-2">
        <div className={label} style={{ color: 'var(--subtle)' }}>Mål per dag</div>
        <div className="flex items-center gap-2">
          <span className={field} style={{ ...fieldStyle, lineHeight: '2rem', width: '4.5rem', textAlign: 'right' }}>
            {hours(month.dailyHours)} h
          </span>
          <span className="text-[11px] leading-snug" style={{ color: 'var(--subtle)' }}>
            Från inställningar. Röda dagar och dagen före kortas automatiskt.
          </span>
        </div>
      </div>

      <div className="h-px" style={{ background: 'var(--border)' }} />

      <div className="flex flex-col gap-2">
        <div className={label} style={{ color: 'var(--subtle)' }}>Fördelning per dag</div>
        {lines.map((line, index) => (
          <div key={index} className="flex items-center gap-1.5">
            <select
              aria-label={`Work item för rad ${index + 1}`}
              className={`${field} min-w-0 flex-1`}
              style={fieldStyle}
              value={line.workItemId}
              onChange={(event) => update(index, { workItemId: Number(event.target.value) })}
            >
              {workItems.map((item) => (
                <option key={item.id} value={item.id}>#{item.id} {item.name}</option>
              ))}
            </select>
            <input
              type="number" min={0} step={0.25}
              aria-label={`Timmar för rad ${index + 1}`}
              className={`${field} w-13 text-right`}
              style={fieldStyle}
              value={line.hours}
              onChange={(event) => update(index, { hours: Number(event.target.value) || 0 })}
            />
            <button
              type="button"
              aria-label={`Ta bort rad ${index + 1}`}
              disabled={lines.length === 1}
              className="flex size-7 items-center justify-center rounded-md disabled:opacity-40"
              style={{ color: 'var(--subtle)' }}
              onClick={() => setLines((current) => current.filter((_, i) => i !== index))}
            >
              <Close />
            </button>
          </div>
        ))}
        <div className="flex items-center justify-between gap-2">
          <button
            type="button"
            className="flex h-7 items-center gap-1.5 rounded-md border border-dashed px-2 text-xs"
            style={{ borderColor: 'var(--border)', color: 'var(--accent)' }}
            onClick={() => favourite && setLines((current) => [...current, { workItemId: favourite.id, hours: 0 }])}
          >
            <Plus /> Lägg till work item
          </button>
          <span
            className="flex items-center gap-1 text-xs"
            style={{ color: balanced ? 'var(--ok)' : 'var(--warn)' }}
          >
            {balanced && <Check />}
            {hours(linesSum)} av {hours(month.dailyHours)} h
          </span>
        </div>
      </div>

      <div className="h-px" style={{ background: 'var(--border)' }} />

      <div className="flex flex-col gap-2">
        <div className={label} style={{ color: 'var(--subtle)' }}>Fylls upp till målet</div>
        {blocked ? (
          <Row label={`${selected.length} valda dagar`} value="okänt" color="var(--subtle)" />
        ) : (
          <>
            {summary.emptyDays > 0 && (
              <Row
                label={`${summary.emptyDays} ${summary.emptyDays === 1 ? 'tom dag' : 'tomma dagar'}`}
                value={`${hours(summary.emptyDays * month.dailyHours)} h`}
              />
            )}
            {summary.partialDays > 0 && (
              <Row
                label={`${summary.partialDays} delvis ${summary.partialDays === 1 ? 'dag' : 'dagar'}`}
                value={`${hours(summary.totalHours - summary.emptyDays * month.dailyHours)} h`}
                color="var(--warn)"
              />
            )}
            {summary.skippedDays > 0 && (
              <Row
                label={`${summary.skippedDays} ${summary.skippedDays === 1 ? 'dag' : 'dagar'} redan klar`}
                value="hoppas över"
                color="var(--subtle)"
              />
            )}
          </>
        )}
        <div className="mt-0.5 flex items-baseline justify-between">
          <span className="text-[13px]">Att registrera</span>
          <span className="text-[22px] font-semibold" style={{ color: blocked ? 'var(--subtle)' : 'var(--fg)' }}>
            {blocked ? '— h' : `${hours(summary.totalHours)} h`}
          </span>
        </div>
      </div>

      {error && (
        <div className="rounded-md p-2 text-xs" style={{ background: 'var(--danger-bg)', color: 'var(--danger)' }}>
          {error}
        </div>
      )}

      {result && (
        <div className="flex flex-col gap-1 text-xs">
          {/* Simulate never posts, so the outcome message must never read like a real post
              did happen — that is the one mistake here that is not undoable by hand. */}
          {resultSimulated && (
            <span className="font-semibold" style={{ color: 'var(--warn)' }}>
              Simulering – inget skickades till 7Pace.
            </span>
          )}
          <span style={{ color: 'var(--subtle)' }}>
            {resultSimulated
              ? `${result.postedEntries} poster skulle ha registrerats${result.failedEntries > 0 ? `, ${result.failedEntries} skulle ha misslyckats` : ''}.`
              : result.failedEntries === 0
                ? `${result.postedEntries} poster registrerade.`
                : `${result.postedEntries} registrerade, ${result.failedEntries} misslyckades.`}
          </span>
          {result.days.filter((d) => d.status !== 'ok').map((day) => (
            <span key={day.date} style={{ color: 'var(--danger)' }}>{day.date}: {day.error}</span>
          ))}
        </div>
      )}

      <div className="mt-auto flex flex-col gap-2.5">
        <label className="flex items-center gap-2 text-[13px]">
          <input type="checkbox" checked={simulate} onChange={(event) => setSimulate(event.target.checked)} />
          Simulera (skicka inget)
        </label>
        <button
          type="button"
          disabled={!canRegister}
          onClick={() => void register()}
          className="flex h-9.5 items-center justify-center gap-1.5 rounded-md border text-sm font-semibold disabled:opacity-50"
          style={{
            // Border colour is the visible tell that this run will not touch 7Pace — the fill
            // stays the proven accent/accent-fg contrast pair rather than a re-coloured one.
            borderColor: simulate ? 'var(--warn)' : 'var(--accent)',
            background: 'var(--accent)',
            color: 'var(--accent-fg)',
          }}
        >
          {simulate && !busy && <Warning />}
          {busy
            ? (simulate ? 'Simulerar…' : 'Registrerar…')
            : blocked
              ? 'Registrera'
              : `Registrera ${hours(summary.totalHours)} h${simulate ? ' (simulering)' : ''}`}
        </button>
        <span className="text-center text-[11px]" style={{ color: 'var(--subtle)' }}>
          {blocked ? 'Blockerad tills tiden är hämtad' : 'Kalendern hämtas om från 7Pace efteråt'}
        </span>
        {selected.length > 0 && (
          <button type="button" className="text-[11px] underline" style={{ color: 'var(--subtle)' }} onClick={onClear}>
            Rensa markering
          </button>
        )}
      </div>
    </aside>
  )
}

function Row({ label, value, color = 'var(--fg)' }: { label: string; value: string; color?: string }) {
  return (
    <div className="flex items-baseline justify-between gap-2 text-[13px]">
      <span style={{ color: 'var(--subtle)' }}>{label}</span>
      <span className="font-medium" style={{ color }}>{value}</span>
    </div>
  )
}
