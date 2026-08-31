import type { Day, DayStatus } from '../types'
import { hours } from '../dates'

const STRIPE: Record<DayStatus, string> = {
  complete: 'var(--ok)',
  partial: 'var(--warn)',
  empty: 'var(--idle)',
  over: 'var(--over)',
  unknown: 'var(--subtle)',
  nonWorking: 'transparent',
}

interface Props {
  day: Day
  plannedHours: number
  selected: boolean
  onPointerDown?: (event: React.PointerEvent) => void
  onPointerEnter?: (event: React.PointerEvent) => void
  onKeyDown?: (event: React.KeyboardEvent) => void
  tabIndex?: number
}

// Same vocabulary the visible Legend uses (Tom/Delvis/Klar/Över), lower-cased to read as a
// trailing descriptor in the spoken sentence rather than a standalone label.
const STATUS_WORD: Record<'empty' | 'partial' | 'complete' | 'over', string> = {
  empty: 'tom', partial: 'delvis', complete: 'klar', over: 'över',
}

function accessibleName(day: Day, plannedHours: number): string {
  const date = day.date
  if (day.status === 'nonWorking') return `${date}, ledig${day.holidayName ? `, ${day.holidayName}` : ''}`
  if (day.status === 'unknown') return `${date}, registrerad tid ej hämtad`
  const base = `${date}, ${hours(day.logged)} av ${hours(day.expected)} timmar, ${STATUS_WORD[day.status]}`
  return plannedHours > 0 ? `${base}, planerat ${hours(plannedHours)} timmar` : base
}

export function DayCell({
  day, plannedHours, selected, onPointerDown, onPointerEnter, onKeyDown, tabIndex = -1,
}: Props) {
  const nonWorking = day.status === 'nonWorking'
  const unknown = day.status === 'unknown'

  return (
    <button
      type="button"
      role="button"
      aria-pressed={selected}
      aria-label={accessibleName(day, plannedHours)}
      data-date={day.date}
      tabIndex={tabIndex}
      onPointerDown={onPointerDown}
      onPointerEnter={onPointerEnter}
      onKeyDown={onKeyDown}
      className="relative flex cursor-pointer flex-col overflow-hidden rounded-lg border p-2 text-left"
      style={{
        background: selected
          ? 'var(--sel-bg)'
          : unknown
            ? 'repeating-linear-gradient(135deg, var(--row-alt) 0 6px, var(--surface) 6px 12px)'
            : nonWorking ? 'var(--row-alt)' : 'var(--surface)',
        borderColor: selected ? 'var(--accent)' : 'var(--border)',
        boxShadow: selected ? 'inset 0 0 0 1px var(--accent)' : undefined,
        opacity: day.inMonth ? 1 : 0.4,
      }}
    >
      <span className="absolute inset-y-0 left-0 w-[3px]" style={{ background: STRIPE[day.status] }} />

      <span className="flex items-start justify-between gap-1.5">
        <span className="text-[15px] font-semibold" style={{ color: day.inMonth ? 'var(--fg)' : 'var(--subtle)' }}>
          {Number(day.date.slice(8, 10))}
        </span>
        {selected && plannedHours > 0 && (
          <span
            className="rounded px-1.5 py-0.5 text-[10px] font-semibold"
            style={{ background: 'var(--plan-bg)', color: 'var(--accent)' }}
          >
            +{hours(plannedHours)} h
          </span>
        )}
        {selected && plannedHours === 0 && !nonWorking && !unknown && (
          <span className="rounded px-1.5 py-0.5 text-[10px]" style={{ background: 'var(--chip)', color: 'var(--subtle)' }}>
            klar
          </span>
        )}
      </span>

      {nonWorking ? (
        <span className="mt-auto text-[11px]" style={{ color: 'var(--subtle)' }}>
          {day.holidayName ?? (day.inMonth ? 'Helg' : '')}
        </span>
      ) : unknown ? (
        <>
          <span className="mt-auto flex items-baseline gap-0.5">
            <span className="text-lg font-semibold leading-tight" style={{ color: 'var(--subtle)' }}>?</span>
            <span className="text-xs" style={{ color: 'var(--subtle)' }}>/ {hours(day.expected)} h</span>
          </span>
          <span className="mt-1 text-[10px]" style={{ color: 'var(--subtle)' }}>ej hämtad</span>
        </>
      ) : (
        <>
          <span className="mt-auto flex items-baseline gap-0.5">
            <span
              className="text-lg font-semibold leading-tight"
              style={{ color: day.status === 'empty' ? 'var(--subtle)' : 'var(--fg)' }}
            >
              {hours(day.logged)}
            </span>
            <span className="text-xs" style={{ color: 'var(--subtle)' }}>/ {hours(day.expected)} h</span>
          </span>
          <span className="mt-1 flex min-h-4 flex-wrap gap-1">
            {day.existing.slice(0, 3).map((log) => (
              <span
                key={log.id}
                title={log.workItemName ?? undefined}
                className="rounded px-1.5 py-0.5 text-[10px]"
                style={{ background: 'var(--chip)', color: 'var(--subtle)' }}
              >
                #{log.workItemId}
              </span>
            ))}
          </span>
        </>
      )}
    </button>
  )
}
