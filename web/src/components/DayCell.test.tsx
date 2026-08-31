import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DayCell } from './DayCell'
import type { Day, DayStatus } from '../types'

const day = (over: Partial<Day> = {}): Day => ({
  date: '2026-06-03', expected: 8, logged: 6, remaining: 2, status: 'partial',
  hitZeroFloor: false, isoWeek: 23, inMonth: true, holidayName: null,
  existing: [{ id: 'a', hours: 6, workItemId: 12345, workItemName: 'Sprintarbete', comment: null }],
  ...over,
})

describe('DayCell', () => {
  it('shows logged over expected hours', () => {
    render(<DayCell day={day()} plannedHours={0} selected={false} />)

    expect(screen.getByText('6')).toBeInTheDocument()
    expect(screen.getByText('/ 8 h')).toBeInTheDocument()
  })

  it('shows the work item id of existing time', () => {
    render(<DayCell day={day()} plannedHours={0} selected={false} />)

    expect(screen.getByText('#12345')).toBeInTheDocument()
  })

  it('badges the planned top-up', () => {
    render(<DayCell day={day()} plannedHours={5} selected />)

    expect(screen.getByText('+5 h')).toBeInTheDocument()
  })

  it('badges a selected day that is already complete as skipped', () => {
    render(<DayCell day={day({ status: 'complete', logged: 8, remaining: 0 })} plannedHours={0} selected />)

    expect(screen.getByText('klar')).toBeInTheDocument()
  })

  it('names a holiday instead of hours', () => {
    render(
      <DayCell
        day={day({ status: 'nonWorking', expected: 0, logged: 0, remaining: 0, holidayName: 'Midsommarafton', existing: [] })}
        plannedHours={0}
        selected={false}
      />,
    )

    expect(screen.getByText('Midsommarafton')).toBeInTheDocument()
    expect(screen.queryByText('/ 0 h')).not.toBeInTheDocument()
  })

  it('marks an unknown day as not fetched rather than empty', () => {
    render(<DayCell day={day({ status: 'unknown', logged: 0, remaining: 8, existing: [] })} plannedHours={0} selected={false} />)

    expect(screen.getByText('?')).toBeInTheDocument()
    expect(screen.getByText('ej hämtad')).toBeInTheDocument()
  })

  it('states the hours in text for every status, so colour is never the only cue', () => {
    const statuses: DayStatus[] = ['empty', 'partial', 'complete', 'over']

    for (const status of statuses) {
      const { unmount } = render(
        <DayCell day={day({ status, logged: 4, remaining: 4 })} plannedHours={0} selected={false} />,
      )
      expect(screen.getByRole('button')).toHaveAccessibleName(/4/)
      unmount()
    }
  })

  it('names the status in the accessible name too, not just the numbers, for every status', () => {
    // The spec requires the accessible name to give date, hours AND status. Matching only /4/
    // (the old assertion above) would pass even if a screen-reader user could only infer
    // complete-vs-partial-vs-over by comparing two spoken numbers - this pins the actual word,
    // using the same vocabulary the visible Legend already uses (Tom/Delvis/Klar/Över).
    const expectations: [DayStatus, RegExp][] = [
      ['empty', /tom/i],
      ['partial', /delvis/i],
      ['complete', /klar/i],
      ['over', /över/i],
    ]

    for (const [status, word] of expectations) {
      const { unmount } = render(
        <DayCell day={day({ status, logged: 4, remaining: 4 })} plannedHours={0} selected={false} />,
      )
      expect(screen.getByRole('button')).toHaveAccessibleName(word)
      unmount()
    }
  })

  it('exposes selection state to assistive technology', () => {
    render(<DayCell day={day()} plannedHours={0} selected />)

    expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true')
  })
})
