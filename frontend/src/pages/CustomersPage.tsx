import { useState } from 'react'
import { listCustomers } from '../api/catalog'
import Pager from '../components/Pager'
import Status from '../components/Status'
import { useFetch } from '../hooks/useFetch'

export default function CustomersPage() {
  const [page, setPage] = useState(1)
  const [q, setQ] = useState('')
  const { data, loading, error } = useFetch(() => listCustomers(page, q), [page, q])

  return (
    <section>
      <h1>Customers</h1>
      <input placeholder="Search name or email" value={q} onChange={(e) => { setPage(1); setQ(e.target.value) }} />
      <Status loading={loading} error={error} empty={data?.items.length === 0} emptyText="No customers match.">
        {data && (
          <>
            <table>
              <thead>
                <tr><th>Name</th><th>Email</th><th>Tier</th><th>Region</th></tr>
              </thead>
              <tbody>
                {data.items.map((c) => (
                  <tr key={c.id}>
                    <td>{c.firstName} {c.lastName}</td><td>{c.email}</td><td>{c.loyaltyTier}</td><td>{c.region}</td>
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
