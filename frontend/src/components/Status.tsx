import type { ReactNode } from 'react'

interface Props {
  loading: boolean
  error: string | null
  empty?: boolean
  emptyText?: string
  children: ReactNode
}

export default function Status({ loading, error, empty, emptyText, children }: Props) {
  if (loading) return <p className="muted">Loading...</p>
  if (error) return <p className="error">Something went wrong: {error}</p>
  if (empty) return <p className="muted">{emptyText ?? 'Nothing to show.'}</p>
  return <>{children}</>
}
