import { useEffect } from 'react'
import type { Theme } from './types'

/**
 * Applies the three-way theme choice: System follows prefers-color-scheme, Light and Dark
 * pin it. The `dark` class on <html> is what theme.css keys off.
 */
export function useTheme(theme: Theme) {
  useEffect(() => {
    const query = window.matchMedia('(prefers-color-scheme: dark)')

    const apply = () => {
      const dark = theme === 'Dark' || (theme === 'System' && query.matches)
      document.documentElement.classList.toggle('dark', dark)
    }

    apply()
    if (theme !== 'System') return
    query.addEventListener('change', apply)
    return () => query.removeEventListener('change', apply)
  }, [theme])
}
