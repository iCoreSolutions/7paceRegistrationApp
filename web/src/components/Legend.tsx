const ITEMS = [
  ['var(--ok)', 'Klar'],
  ['var(--warn)', 'Delvis'],
  ['var(--idle)', 'Tom'],
  ['var(--over)', 'Över'],
  ['var(--row-alt)', 'Ledig'],
] as const

export function Legend() {
  return (
    <div className="flex items-center gap-3.5">
      {ITEMS.map(([color, label]) => (
        <span key={label} className="flex items-center gap-1.5 text-[11px]" style={{ color: 'var(--subtle)' }}>
          <span className="size-2 rounded-sm" style={{ background: color }} />
          {label}
        </span>
      ))}
    </div>
  )
}
