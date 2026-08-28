import '@testing-library/jest-dom/vitest'

// jsdom does not implement matchMedia. useTheme() (wired into App in Task 14) reads
// prefers-color-scheme via it, so every test that mounts App needs this stub.
if (typeof window.matchMedia !== 'function') {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia
}
