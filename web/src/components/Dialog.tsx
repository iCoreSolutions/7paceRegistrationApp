import { useEffect, useRef, type ReactNode } from 'react'
import { Close } from './Icons'

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]),' +
  ' textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

export function Dialog({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const panelRef = useRef<HTMLDivElement>(null)

  // Escape closes the dialog, and Tab/Shift+Tab is trapped inside it so a keyboard user
  // can never tab out into the (still-mounted, but hidden-behind-the-overlay) page.
  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    const panel = panelRef.current
    const focusable = panel?.querySelectorAll<HTMLElement>(FOCUSABLE)
    ;(focusable?.[0] ?? panel)?.focus()

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
        return
      }
      if (event.key !== 'Tab' || !panel) return
      const elements = panel.querySelectorAll<HTMLElement>(FOCUSABLE)
      if (elements.length === 0) return
      const first = elements[0]
      const last = elements[elements.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      previouslyFocused?.focus()
    }
  }, [onClose])

  return (
    <div className="fixed inset-0 z-10 flex items-center justify-center bg-black/40 p-4">
      <div
        ref={panelRef}
        role="dialog" aria-modal="true" aria-label={title} tabIndex={-1}
        className="flex max-h-full w-full max-w-lg flex-col gap-4 overflow-y-auto rounded-xl border p-5 outline-none"
        style={{ borderColor: 'var(--border)', background: 'var(--surface)' }}
      >
        <div className="flex items-center justify-between">
          <h2 className="text-base font-semibold">{title}</h2>
          <button
            type="button" aria-label="Stäng" onClick={onClose}
            className="flex size-7 items-center justify-center rounded-md"
            style={{ color: 'var(--subtle)' }}
          >
            <Close />
          </button>
        </div>
        {children}
      </div>
    </div>
  )
}
