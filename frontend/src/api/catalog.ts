import { getJson } from './http'
import type { Customer, DailySales, Paged, Product, StockLevel, Store } from './types'

export function listProducts(page: number, q: string) {
  const params = new URLSearchParams({ page: String(page), pageSize: '25' })
  if (q) params.set('q', q)
  return getJson<Paged<Product>>(`/api/products?${params}`)
}

export function listStores() {
  return getJson<Store[]>('/api/stores')
}

export function listStoreStock(storeId: number) {
  return getJson<StockLevel[]>(`/api/stores/${storeId}/stock?lowOnly=true`)
}

export function listCustomers(page: number, q: string) {
  const params = new URLSearchParams({ page: String(page), pageSize: '25' })
  if (q) params.set('q', q)
  return getJson<Paged<Customer>>(`/api/customers?${params}`)
}

export function getDailySales(date: string) {
  return getJson<DailySales>(`/api/reports/daily-sales?date=${date}`)
}
