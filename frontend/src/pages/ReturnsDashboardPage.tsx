import { useState } from 'react'
import { getReturnsDashboard } from '../api/returns'
import Status from '../components/Status'
import { useFetch } from '../hooks/useFetch'
import type { ReturnRateRow } from '../api/types'
import { money } from '../utils'

const iso = (d: Date) => d.toISOString().slice(0, 10)

function RateTable({ title, rows }: { title: string; rows: ReturnRateRow[] }) {
  return (
    <>
      <h2>{title}</h2>
      {rows.length === 0 ? (
        <p className="muted">No data in this period.</p>
      ) : (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th className="num">Sold</th>
              <th className="num">Returned</th>
              <th className="num">Return rate</th>
              <th className="num">Refunded</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.key}>
                <td>{r.label}</td>
                <td className="num">{r.soldQuantity}</td>
                <td className="num">{r.returnedQuantity}</td>
                <td className="num">{r.returnRatePct.toFixed(1)}%</td>
                <td className="num">{money(r.refundTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}

export default function ReturnsDashboardPage() {
  const today = new Date()
  const [from, setFrom] = useState(iso(new Date(today.getTime() - 90 * 86400000)))
  const [to, setTo] = useState(iso(today))
  const { data, loading, error } = useFetch(() => getReturnsDashboard(from, to), [from, to])

  return (
    <section>
      <h1>Returns dashboard</h1>
      <div className="row">
        <label>
          From
          <input type="date" value={from} max={to} onChange={(e) => e.target.value && setFrom(e.target.value)} />
        </label>
        <label>
          To
          <input type="date" value={to} min={from} onChange={(e) => e.target.value && setTo(e.target.value)} />
        </label>
      </div>
      <Status loading={loading} error={error}>
        {data && (
          <>
            <RateTable title="By product" rows={data.byProduct} />
            <RateTable title="By category" rows={data.byCategory} />
            <RateTable title="By store" rows={data.byStore} />
            <h2>Flagged customers</h2>
            {data.flaggedCustomers.length === 0 ? (
              <p className="muted">No customers are currently flagged.</p>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>Customer</th>
                    <th className="num">Returns</th>
                    <th className="num">Orders</th>
                    <th className="num">Returned value</th>
                    <th className="num">Purchased value</th>
                    <th className="num">Rate</th>
                  </tr>
                </thead>
                <tbody>
                  {data.flaggedCustomers.map((c) => (
                    <tr key={c.customerId}>
                      <td>{c.customerName || `Customer #${c.customerId}`}</td>
                      <td className="num">{c.rmaCount}</td>
                      <td className="num">{c.distinctOrders}</td>
                      <td className="num">{money(c.returnedValue)}</td>
                      <td className="num">{money(c.purchasedValue)}</td>
                      <td className="num">{c.ratePct.toFixed(1)}%</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </Status>
    </section>
  )
}
