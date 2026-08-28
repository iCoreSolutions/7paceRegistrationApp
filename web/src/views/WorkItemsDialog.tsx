import { useState } from 'react'
import { api, ApiError } from '../api'
import type { WorkItem } from '../types'
import { Dialog } from '../components/Dialog'
import { Check, Close, Plus } from '../components/Icons'

interface Props {
  items: WorkItem[]
  onSaved: (items: WorkItem[]) => void
  onClose: () => void
}

export function WorkItemsDialog({ items, onSaved, onClose }: Props) {
  const [draft, setDraft] = useState<WorkItem[]>(items)
  const [newId, setNewId] = useState('')
  const [newName, setNewName] = useState('')
  const [error, setError] = useState<string | null>(null)

  const setFavourite = (id: number) =>
    setDraft((current) => current.map((item) => ({ ...item, isFavorite: item.id === id })))

  /** Removing the favourite hands the role to the first survivor, so exactly one always holds it. */
  const remove = (id: number) =>
    setDraft((current) => {
      const rest = current.filter((item) => item.id !== id)
      return rest.some((item) => item.isFavorite)
        ? rest
        : rest.map((item, index) => ({ ...item, isFavorite: index === 0 }))
    })

  const add = () => {
    const id = Number(newId)
    if (id <= 0 || newName.trim() === '') return
    if (draft.some((item) => item.id === id)) {
      setError('Det work itemet finns redan.')
      return
    }
    setDraft((current) => [...current, { id, name: newName.trim(), isFavorite: current.length === 0 }])
    setNewId('')
    setNewName('')
    setError(null)
  }

  async function save() {
    try {
      await api.saveWorkItems(draft)
      onSaved(draft)
      onClose()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Okänt fel.')
    }
  }

  const field = 'h-8 rounded-md border px-2 text-[13px]'
  const fieldStyle = { borderColor: 'var(--border)', background: 'var(--surface)', color: 'var(--fg)' }

  return (
    <Dialog title="Work items" onClose={onClose}>
      <div className="flex flex-col gap-1.5">
        {draft.map((item) => (
          <div key={item.id} className="flex items-center gap-2 rounded-md border p-2" style={fieldStyle}>
            <span className="min-w-0 flex-1 truncate text-[13px]">#{item.id} {item.name}</span>
            <button
              type="button"
              aria-label={`Gör ${item.id} till favorit`}
              onClick={() => setFavourite(item.id)}
              className="flex size-7 items-center justify-center rounded-md"
              style={{ color: item.isFavorite ? 'var(--accent)' : 'var(--subtle)' }}
            >
              <Check />
            </button>
            <button
              type="button"
              aria-label={`Ta bort ${item.id}`}
              disabled={draft.length === 1}
              onClick={() => remove(item.id)}
              className="flex size-7 items-center justify-center rounded-md disabled:opacity-40"
              style={{ color: 'var(--subtle)' }}
            >
              <Close />
            </button>
          </div>
        ))}
      </div>

      <div className="flex items-end gap-2">
        <label className="flex flex-col gap-1">
          <span className="text-[11px]" style={{ color: 'var(--subtle)' }}>Nytt work item-ID</span>
          <input type="number" className={`${field} w-28`} style={fieldStyle} value={newId}
                 onChange={(e) => setNewId(e.target.value)} />
        </label>
        <label className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="text-[11px]" style={{ color: 'var(--subtle)' }}>Nytt namn</span>
          <input className={field} style={fieldStyle} value={newName} onChange={(e) => setNewName(e.target.value)} />
        </label>
        <button
          type="button" onClick={add}
          className="flex h-8 items-center gap-1.5 rounded-md border px-2 text-xs"
          style={{ borderColor: 'var(--border)', color: 'var(--accent)' }}
        >
          <Plus /> Lägg till
        </button>
      </div>

      {error && <div className="text-xs" style={{ color: 'var(--danger)' }}>{error}</div>}

      <button
        type="button" onClick={() => void save()}
        className="h-9 rounded-md border text-sm font-semibold"
        style={{ borderColor: 'var(--accent)', background: 'var(--accent)', color: 'var(--accent-fg)' }}
      >
        Spara
      </button>
    </Dialog>
  )
}
