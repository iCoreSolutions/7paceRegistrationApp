import type { Month } from '../types'
import { formatMonth, hours } from '../dates'

export function StatusBar({ month }: { month: Month }) {
  const percent = month.totals.expected > 0
    ? Math.min(100, (month.totals.logged / month.totals.expected) * 100)
    : 0

  return (
    <div
      className="flex h-11 items-center gap-4 border-t px-4"
      style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
    >
      <span className="text-xs" style={{ color: 'var(--subtle)' }}>
        {formatMonth(month.year, month.month)}
      </span>
      <span className="text-[13px]">
        <strong className="font-semibold">{hours(month.totals.logged)}</strong> av{' '}
        {hours(month.totals.expected)} h loggade
      </span>
      <div className="flex h-1.5 flex-1 overflow-hidden rounded-full" style={{ background: 'var(--track)' }}>
        <div style={{ width: `${percent}%`, background: 'var(--accent)' }} />
      </div>
      <span className="text-[13px] font-semibold" style={{ color: 'var(--warn)' }}>
        {hours(month.totals.missing)} h saknas
      </span>
    </div>
  )
}
