import { useState } from 'react'
import { approveReturn, createReturn, getEligibility, listUsers, rejectReturn } from '../api/returns'
import { listStores } from '../api/catalog'
import Status from '../components/Status'
import { useFetch } from '../hooks/useFetch'
import type { Eligibility, ReturnGrade, ReturnRecord } from '../api/types'
import { money } from '../utils'

const GRADES: { value: ReturnGrade; label: string }[] = [
  { value: 'A', label: 'A - as new, restocked' },
  { value: 'B', label: 'B - good, restocked' },
  { value: 'C', label: 'C - worn, reduced refund, not restocked' },
  { value: 'SCRAP', label: 'Scrap - supplier claim, not restocked' },
]

interface LineInput {
  quantity: number
  grade: ReturnGrade
}

export default function RmaDeskPage() {
  const stores = useFetch(listStores, [])
  const users = useFetch(listUsers, [])

  const [orderNumber, setOrderNumber] = useState('')
  const [elig, setElig] = useState<Eligibility | null>(null)
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState<string | null>(null)

  const [inputs, setInputs] = useState<Record<number, LineInput>>({})
  const [storeId, setStoreId] = useState<number | ''>('')
  const [userId, setUserId] = useState<number | ''>('')

  const [draft, setDraft] = useState<ReturnRecord | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const activeUsers = (users.data ?? []).filter((u) => u.isActive)

  // Drop the local draft. If it is still a DRAFT server-side, quietly reject it;
  // failure is ignored because the next Calculate supersedes it anyway.
  function discardLocalDraft() {
    if (draft && draft.status === 'DRAFT' && userId !== '') {
      rejectReturn(draft.id, userId).catch(() => {})
    }
    setDraft(null)
    setNotice(null)
  }

  async function search(e: React.FormEvent) {
    e.preventDefault()
    const n = orderNumber.trim()
    if (!n) return
    setSearching(true)
    setSearchError(null)
    setElig(null)
    discardLocalDraft()
    setActionError(null)
    try {
      const result = await getEligibility(n)
      setElig(result)
      setStoreId(result.orderStoreId)
      const init: Record<number, LineInput> = {}
      result.lines.forEach((l) => {
        init[l.orderLineId] = { quantity: 0, grade: 'A' }
      })
      setInputs(init)
    } catch (err) {
      setSearchError((err as Error).message)
    } finally {
      setSearching(false)
    }
  }

  function setLine(id: number, patch: Partial<LineInput>) {
    discardLocalDraft() // any change invalidates the calculated refund
    setActionError(null)
    setInputs((cur) => ({ ...cur, [id]: { ...cur[id], ...patch } }))
  }

  const chosen = elig ? elig.lines.filter((l) => (inputs[l.orderLineId]?.quantity ?? 0) > 0) : []
  const canCalculate = !!elig && chosen.length > 0 && storeId !== '' && userId !== '' && !busy

  async function calculate() {
    if (!elig || storeId === '' || userId === '') return
    setBusy(true)
    setActionError(null)
    setNotice(null)
    try {
      // always replace the held draft with the newly returned one (older drafts are superseded server-side)
      const created = await createReturn({
          orderNumber: elig.orderNumber,
          storeId,
          createdByUserId: userId,
          lines: chosen.map((l) => ({
            orderLineId: l.orderLineId,
            quantity: inputs[l.orderLineId].quantity,
            grade: inputs[l.orderLineId].grade,
          })),
        })
      setDraft(created)
    } catch (err) {
      setActionError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function approve() {
    if (!draft || userId === '') return
    setBusy(true)
    setActionError(null)
    try {
      setDraft(await approveReturn(draft.id, userId))
    } catch (err) {
      setActionError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function discard() {
    if (!draft || userId === '') return
    setBusy(true)
    setActionError(null)
    try {
      const rma = draft.rmaNumber
      await rejectReturn(draft.id, userId)
      setDraft(null)
      setNotice(`Draft ${rma} discarded. No refund was issued.`)
    } catch (err) {
      setActionError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const user = activeUsers.find((u) => u.id === userId)
  const isManager = user?.role === 'MANAGER' || user?.role === 'ADMIN'

  return (
    <section>
      <h1>RMA desk</h1>
      <form className="row" onSubmit={search}>
        <label>
          Order number
          <input value={orderNumber} onChange={(e) => setOrderNumber(e.target.value)} placeholder="Order number" />
        </label>
        <button type="submit" disabled={searching || !orderNumber.trim()}>Check order</button>
      </form>

      <Status loading={searching} error={searchError} empty={!elig} emptyText="Enter an order number to see what can be returned.">
        {elig && (
          <>
            <p>
              Order <strong>{elig.orderNumber}</strong> for <strong>{elig.customerName}</strong> ({elig.orderStatus}).
              Paid {money(elig.capturedPayments)}, already refunded {money(elig.refundedTotal)}, refundable remaining{' '}
              {money(elig.orderRefundableRemaining)}.
            </p>

            {elig.serialReturner && (
              <div className="banner warn" role="alert">
                <strong>Serial returner.</strong> This customer has a high recent return rate. This is a heads-up
                only: the return can still be processed and refunded as normal.
              </div>
            )}

            {!elig.eligible && <div className="banner bad">Not returnable: {elig.reason ?? 'no reason given'}</div>}

            <table>
              <thead>
                <tr>
                  <th>Item</th>
                  <th className="num">Ordered</th>
                  <th className="num">Returnable</th>
                  <th className="num">Refundable up to</th>
                  <th>Return until</th>
                  <th>Return qty</th>
                  <th>Condition</th>
                </tr>
              </thead>
              <tbody>
                {elig.lines.map((l) => {
                  const inp = inputs[l.orderLineId] ?? { quantity: 0, grade: 'A' as ReturnGrade }
                  const can = l.eligible && l.returnableQuantity > 0
                  return (
                    <tr key={l.orderLineId}>
                      <td>
                        {l.productName} <span className="muted">({l.sku})</span>
                        {!can && l.reason && <div className="muted">{l.reason}</div>}
                      </td>
                      <td className="num">{l.quantityOrdered}</td>
                      <td className="num">{l.returnableQuantity}</td>
                      <td className="num">{money(l.returnableAmount)}</td>
                      <td>{l.returnableUntil}</td>
                      <td>
                        <input
                          className="num-input"
                          type="number"
                          min={0}
                          max={l.returnableQuantity}
                          disabled={!can}
                          value={inp.quantity}
                          aria-label={`Return quantity for ${l.sku}`}
                          onChange={(e) =>
                            setLine(l.orderLineId, {
                              quantity: Math.max(0, Math.min(l.returnableQuantity, Math.floor(Number(e.target.value) || 0))),
                            })
                          }
                        />
                      </td>
                      <td>
                        <select
                          disabled={!can}
                          value={inp.grade}
                          aria-label={`Condition for ${l.sku}`}
                          onChange={(e) => setLine(l.orderLineId, { grade: e.target.value as ReturnGrade })}
                        >
                          {GRADES.map((g) => (
                            <option key={g.value} value={g.value}>{g.label}</option>
                          ))}
                        </select>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>

            <div className="row">
              <label>
                Receiving store
                <select
                  value={storeId}
                  onChange={(e) => {
                    setStoreId(e.target.value ? Number(e.target.value) : '')
                    discardLocalDraft()
                  }}
                >
                  <option value="">Select store...</option>
                  {(stores.data ?? []).map((s) => (
                    <option key={s.id} value={s.id}>{s.name}</option>
                  ))}
                </select>
              </label>
              <label>
                Acting user
                <select
                  value={userId}
                  onChange={(e) => {
                    setUserId(e.target.value ? Number(e.target.value) : '')
                    discardLocalDraft()
                  }}
                >
                  <option value="">Select user...</option>
                  {activeUsers.map((u) => (
                    <option key={u.id} value={u.id}>{u.displayName} ({u.role})</option>
                  ))}
                </select>
              </label>
              <button onClick={calculate} disabled={!canCalculate}>Calculate refund</button>
            </div>
            {(stores.error || users.error) && (
              <p className="error">Could not load stores or users: {stores.error ?? users.error}</p>
            )}
            {chosen.length === 0 && <p className="muted">Enter a quantity on at least one line to calculate a refund.</p>}

            {actionError && <div className="banner bad" role="alert">{actionError}</div>}
            {notice && <div className="banner" role="status">{notice}</div>}

            {draft && (
              <div className="banner ok">
                <p>
                  <strong>{draft.rmaNumber}</strong> ({draft.status}) - refund total <strong>{money(draft.refundTotal)}</strong>
                </p>
                <ul>
                  {draft.lines.map((l) => (
                    <li key={l.id}>
                      {l.productName} x{l.quantity}, grade {l.grade}: {money(l.refundAmount)}
                      {l.grade === 'C' && ' (reduced refund for condition)'}
                      {l.grade === 'SCRAP' && ' (supplier claim, not restocked)'}
                      {l.restocked && ' (restocked)'}
                    </li>
                  ))}
                </ul>
                {draft.status === 'DRAFT' && (
                  <>
                    {draft.requiresManagerApproval && (
                      <p>
                        Refunds above £150 need manager approval.
                        {user && !isManager && ' Select a manager as the acting user to approve.'}
                      </p>
                    )}
                    <button onClick={approve} disabled={busy || userId === ''}>Approve</button>{' '}
                    <button onClick={discard} disabled={busy || userId === ''}>Discard draft</button>
                  </>
                )}
                {draft.status === 'APPROVED' && (
                  <p>Approved. Grade A and B items have been returned to stock at {draft.storeName}.</p>
                )}
              </div>
            )}
          </>
        )}
      </Status>
    </section>
  )
}
