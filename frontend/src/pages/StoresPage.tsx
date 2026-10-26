import { useState } from 'react'
import { listStoreStock, listStores } from '../api/catalog'
import Status from '../components/Status'
import { useFetch } from '../hooks/useFetch'

export default function StoresPage() {
  const [selected, setSelected] = useState<number | null>(null)
  const stores = useFetch(() => listStores(), [])
  const low = useFetch(() => (selected ? listStoreStock(selected) : Promise.resolve([])), [selected])

  return (
    <section>
      <h1>Stores</h1>
      <Status loading={stores.loading} error={stores.error} empty={stores.data?.length === 0}>
        <table>
          <thead>
            <tr><th>Code</th><th>Store</th><th>Region</th><th>Timezone</th><th></th></tr>
          </thead>
          <tbody>
            {stores.data?.map((s) => (
              <tr key={s.id}>
                <td>{s.code}</td><td>{s.name}</td><td>{s.region}</td><td>{s.timezone}</td>
                <td><button onClick={() => setSelected(s.id)}>Low stock</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Status>
      {selected && (
        <>
          <h2>Low stock</h2>
          <Status loading={low.loading} error={low.error} empty={low.data?.length === 0} emptyText="Nothing is low in this store.">
            <ul>
              {low.data?.map((s) => (
                <li key={s.id}>{s.sku} {s.productName}: {s.quantity} left (reorder at {s.reorderLevel})</li>
              ))}
            </ul>
          </Status>
        </>
      )}
    </section>
  )
}
