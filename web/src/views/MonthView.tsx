import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api'
import type { Month } from '../types'
import { WEEKDAYS, addMonths, formatMonth, weekRows } from '../dates'
import { DayCell } from '../components/DayCell'
import { Legend } from '../components/Legend'
import { StatusBar } from '../components/StatusBar'
import { ChevronLeft, ChevronRight, Gear, Moon, Refresh, Warning } from '../components/Icons'

const today = new Date()

export function MonthView() {
  const [period, setPeriod] = useState({ year: today.getFullYear(), month: today.getMonth() + 1 })
  const [month, setMonth] = useState<Month | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setMonth(await api.month(period.year, period.month))
      setLoadError(null)
    } catch (error) {
      setMonth(null)
      setLoadError(error instanceof ApiError ? error.message : 'Okänt fel.')
    }
  }, [period])

  useEffect(() => { void load() }, [load])

  // The header always shows what is actually on screen. Falling back to `period` only
  // matters before the first load resolves (or after a failed one) — once `month` is
  // loaded, its own year/month is the single source of truth for both the title and
  // for where "next"/"previous" navigate from, so they can never drift apart.
  const displayed = month ? { year: month.year, month: month.month } : period

  const button = 'flex h-8 items-center gap-1.5 rounded-md border px-3 text-[13px]'
  const buttonStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <div className="flex h-full flex-col">
      <header
        className="flex h-13 items-center justify-between gap-4 border-b px-4"
        style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
      >
        <div className="flex items-baseline gap-2.5">
          <span className="text-[15px] font-semibold">7Pace Desktop</span>
        </div>
        <div className="flex items-center gap-2">
          {month && (
            <span className="text-xs" style={{ color: 'var(--subtle)' }}>
              Hämtad {new Date(month.fetchedAt).toLocaleTimeString('sv-SE', { hour: '2-digit', minute: '2-digit' })}
            </span>
          )}
          <button type="button" className={button} style={buttonStyle} onClick={() => void load()}>
            <Refresh /> Uppdatera
          </button>
          <button type="button" aria-label="Inställningar" className={button} style={buttonStyle}><Gear /></button>
          <button type="button" aria-label="Tema" className={button} style={buttonStyle}><Moon /></button>
        </div>
      </header>

      <div className="flex h-13 items-center justify-between gap-4 border-b px-4" style={{ borderColor: 'var(--border)' }}>
        <div className="flex items-center gap-2.5">
          <button
            type="button" aria-label="Föregående månad" className={button} style={buttonStyle}
            onClick={() => setPeriod(addMonths(displayed.year, displayed.month, -1))}
          >
            <ChevronLeft />
          </button>
          <span className="min-w-[118px] text-lg font-semibold">{formatMonth(displayed.year, displayed.month)}</span>
          <button
            type="button" aria-label="Nästa månad" className={button} style={buttonStyle}
            onClick={() => setPeriod(addMonths(displayed.year, displayed.month, 1))}
          >
            <ChevronRight />
          </button>
          <button
            type="button" className={button} style={buttonStyle}
            onClick={() => setPeriod({ year: today.getFullYear(), month: today.getMonth() + 1 })}
          >
            Idag
          </button>
        </div>
        <Legend />
      </div>

      {loadError && (
        <div className="flex items-center gap-2 px-4 py-2 text-[13px]" style={{ color: 'var(--danger)' }}>
          <Warning /> {loadError}
        </div>
      )}

      {month?.holidayWarning && (
        <div className="px-4 py-2 text-[13px]" style={{ color: 'var(--warn)' }}>{month.holidayWarning}</div>
      )}

      {month?.loadState === 'failed' && (
        <div
          className="mx-4 mt-2 flex gap-2.5 rounded-lg border p-3"
          style={{ borderColor: 'var(--danger)', background: 'var(--danger-bg)' }}
        >
          <span style={{ color: 'var(--danger)' }}><Warning /></span>
          <div className="flex flex-col gap-1">
            <span className="text-[13px] font-semibold">Registrerad tid kunde inte hämtas</span>
            <span className="text-xs leading-relaxed" style={{ color: 'var(--subtle)' }}>
              Appen vet inte vad som redan är loggat och skulle riskera att dubbelregistrera.
              Uppdatera för att försöka igen. {month.error}
            </span>
          </div>
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        {month && (
          <div className="flex min-w-0 flex-1 flex-col gap-1.5 p-4">
            <div className="flex gap-1.5">
              <div className="w-8.5 shrink-0 text-center text-[11px] font-semibold uppercase" style={{ color: 'var(--subtle)' }}>v</div>
              <div className="grid min-w-0 flex-1 grid-cols-7 gap-1.5">
                {WEEKDAYS.map((weekday) => (
                  <div key={weekday} className="px-1 pb-0.5 text-[11px] font-semibold uppercase tracking-wide" style={{ color: 'var(--subtle)' }}>
                    {weekday}
                  </div>
                ))}
              </div>
            </div>

            <div className="flex min-h-0 flex-1 gap-1.5">
              <div className="grid w-8.5 shrink-0 gap-1.5" style={{ gridTemplateRows: `repeat(${weekRows(month.days).length}, minmax(0, 1fr))` }}>
                {weekRows(month.days).map((row) => (
                  <button
                    key={row[0].date}
                    type="button"
                    aria-label={`Vecka ${row[0].isoWeek}`}
                    className="flex items-center justify-center rounded-md text-xs font-semibold"
                    style={{ color: 'var(--subtle)' }}
                  >
                    {row[0].isoWeek}
                  </button>
                ))}
              </div>

              <div
                className="grid min-w-0 flex-1 grid-cols-7 gap-1.5"
                style={{ gridTemplateRows: `repeat(${weekRows(month.days).length}, minmax(0, 1fr))` }}
              >
                {month.days.map((day) => (
                  <DayCell key={day.date} day={day} plannedHours={0} selected={false} />
                ))}
              </div>
            </div>
          </div>
        )}
      </div>

      {month && <StatusBar month={month} />}
    </div>
  )
}
