import { useState } from 'react'
import { Link } from 'react-router-dom'
import { listOrders } from '../api/orders'
import Pager from '../components/Pager'
import Status from '../components/Status'
import { useFetch } from '../hooks/useFetch'
import { dateTime, money } from '../utils'

export default function OrdersPage() {
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState('')
  const { data, loading, error } = useFetch(() => listOrders(page, status || undefined), [page, status])

  return (
    <section>
      <h1>Orders</h1>
      <label>
        Status{' '}
        <select value={status} onChange={(e) => { setPage(1); setStatus(e.target.value) }}>
          <option value="">All</option>
          <option value="PLACED">Placed</option>
          <option value="PAID">Paid</option>
          <option value="FULFILLED">Fulfilled</option>
          <option value="CANCELLED">Cancelled</option>
        </select>
      </label>
      <Status loading={loading} error={error} empty={data?.items.length === 0} emptyText="No orders match.">
        {data && (
          <>
            <table>
              <thead>
                <tr>
                  <th>Order</th>
                  <th>Placed (UK time)</th>
                  <th>Customer</th>
                  <th>Store</th>
                  <th>Channel</th>
                  <th>Status</th>
                  <th className="num">Items</th>
                  <th className="num">Total</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((o) => (
                  <tr key={o.id}>
                    <td><Link to={`/orders/${o.id}`}>{o.orderNumber}</Link></td>
                    <td>{dateTime(o.orderedUtc)}</td>
                    <td>{o.customerName}</td>
                    <td>{o.storeName}</td>
                    <td>{o.channel}</td>
                    <td><span className={`tag ${o.status.toLowerCase()}`}>{o.status}</span></td>
                    <td className="num">{o.itemCount}</td>
                    <td className="num">{money(o.totalAmount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} onChange={setPage} />
          </>
        )}
      </Status>
    </section>
  )
}
