const base = {
  width: 16, height: 16, viewBox: '0 0 24 24', fill: 'none',
  stroke: 'currentColor', strokeWidth: 1.8, strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
}

export const ChevronLeft = () => <svg {...base} width={18} height={18}><path d="M15 5 8 12l7 7" /></svg>
export const ChevronRight = () => <svg {...base} width={18} height={18}><path d="m9 5 7 7-7 7" /></svg>
export const Refresh = () => <svg {...base}><path d="M20 11a8 8 0 1 0-2.3 5.7" /><path d="M20 5v6h-6" /></svg>
export const Gear = () => (
  <svg {...base}>
    <circle cx="12" cy="12" r="3.2" />
    <path d="M12 2.5v2.6M12 18.9v2.6M21.5 12h-2.6M5.1 12H2.5M18.7 5.3l-1.8 1.8M7.1 16.9l-1.8 1.8M18.7 18.7l-1.8-1.8M7.1 7.1 5.3 5.3" />
  </svg>
)
export const Moon = () => <svg {...base}><path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5Z" /></svg>
export const Plus = () => <svg {...base} width={14} height={14}><path d="M12 5v14M5 12h14" /></svg>
export const Close = () => <svg {...base} width={14} height={14}><path d="M18 6 6 18M6 6l12 12" /></svg>
export const Check = () => <svg {...base} width={14} height={14}><path d="m4 12.5 5 5L20 6.5" /></svg>
export const Warning = () => (
  <svg {...base} width={18} height={18}><path d="M12 3.5 2.5 20h19L12 3.5Z" /><path d="M12 10v4" /><path d="M12 17.3v.1" /></svg>
)
