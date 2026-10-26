import { useState } from 'react'
import { getDailySales } from '../api/catalog'
import Status from '../components/Status'
import { useFetch } from '../hooks/useFetch'
import { money } from '../utils'

export default function DailySalesPage() {
  const [date, setDate] = useState('2026-09-30')
  const { data, loading, error } = useFetch(() => getDailySales(date), [date])

  return (
    <section>
      <h1>Daily sales</h1>
      <label>
        Date <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
      </label>
      <Status loading={loading} error={error} empty={data?.orderCount === 0} emptyText="No sales on this date.">
        {data && (
          <>
            <p>
              <strong>{data.orderCount}</strong> orders, net sales <strong>{money(data.netSales)}</strong>
              {' '}(gross {money(data.grossSales)}, discounts {money(data.discounts)})
            </p>
            <h2>By channel</h2>
            <ul>
              {data.byChannel.map((c) => (
                <li key={c.channel}>{c.channel}: {c.orderCount} orders, {money(c.netSales)}</li>
              ))}
            </ul>
            <h2>By store</h2>
            <table>
              <thead><tr><th>Store</th><th className="num">Orders</th><th className="num">Net sales</th></tr></thead>
              <tbody>
                {data.byStore.map((s) => (
                  <tr key={s.storeId}><td>{s.storeName}</td><td className="num">{s.orderCount}</td><td className="num">{money(s.netSales)}</td></tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </Status>
    </section>
  )
}
