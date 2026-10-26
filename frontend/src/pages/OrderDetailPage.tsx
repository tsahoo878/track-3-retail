import { Link, useParams } from 'react-router-dom'
import { getOrder } from '../api/orders'
import Status from '../components/Status'
import { useFetch } from '../hooks/useFetch'
import { dateTime, money } from '../utils'

export default function OrderDetailPage() {
  const { id } = useParams()
  const orderId = Number(id)
  const { data, loading, error } = useFetch(() => getOrder(orderId), [orderId])

  return (
    <section>
      <p><Link to="/">Back to orders</Link></p>
      <Status loading={loading} error={error}>
        {data && (
          <>
            <h1>{data.orderNumber}</h1>
            <p>
              {data.customerName} at {data.storeName} ({data.channel}), placed {dateTime(data.orderedUtc)}.{' '}
              <span className={`tag ${data.status.toLowerCase()}`}>{data.status}</span>
            </p>
            <h2>Lines</h2>
            <table>
              <thead>
                <tr><th>SKU</th><th>Product</th><th className="num">Qty</th><th className="num">Price</th><th className="num">Total</th></tr>
              </thead>
              <tbody>
                {data.lines.map((l) => (
                  <tr key={l.id}>
                    <td>{l.sku}</td><td>{l.productName}</td><td className="num">{l.quantity}</td>
                    <td className="num">{money(l.unitPrice)}</td><td className="num">{money(l.lineTotal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p>Subtotal {money(data.subtotal)}, discount {money(data.discountAmount)}, <strong>total {money(data.totalAmount)}</strong></p>
            <h2>Payments</h2>
            {data.payments.length === 0 ? (
              <p className="muted">No payments yet.</p>
            ) : (
              <ul>
                {data.payments.map((p) => (
                  <li key={p.id}>{p.method} {money(p.amount)} {p.cardLast4 ? `(card ending ${p.cardLast4})` : ''} - {p.status}</li>
                ))}
              </ul>
            )}
            <h2>History</h2>
            <ul>
              {data.history.map((h) => (
                <li key={h.id}>{dateTime(h.changedUtc)}: {h.fromStatus ?? 'new'} to {h.toStatus}</li>
              ))}
            </ul>
          </>
        )}
      </Status>
    </section>
  )
}
