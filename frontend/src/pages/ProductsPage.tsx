import { useState } from 'react'
import { listProducts } from '../api/catalog'
import Pager from '../components/Pager'
import Status from '../components/Status'
import { useFetch } from '../hooks/useFetch'
import { money } from '../utils'

export default function ProductsPage() {
  const [page, setPage] = useState(1)
  const [q, setQ] = useState('')
  const { data, loading, error } = useFetch(() => listProducts(page, q), [page, q])

  return (
    <section>
      <h1>Products</h1>
      <input placeholder="Search name or SKU" value={q} onChange={(e) => { setPage(1); setQ(e.target.value) }} />
      <Status loading={loading} error={error} empty={data?.items.length === 0} emptyText="No products match.">
        {data && (
          <>
            <table>
              <thead>
                <tr><th>SKU</th><th>Name</th><th>Category</th><th>Brand</th><th className="num">Price</th></tr>
              </thead>
              <tbody>
                {data.items.map((p) => (
                  <tr key={p.id}>
                    <td>{p.sku}</td><td>{p.name}</td><td>{p.categoryName}</td><td>{p.brand}</td>
                    <td className="num">{money(p.unitPrice)}</td>
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
