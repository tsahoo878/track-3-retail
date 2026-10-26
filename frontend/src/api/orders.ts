import { getJson, sendJson } from './http'
import type { OrderDetail, OrderListItem, Paged } from './types'

export function listOrders(page: number, status?: string) {
  const params = new URLSearchParams({ page: String(page), pageSize: '25' })
  if (status) params.set('status', status)
  return getJson<Paged<OrderListItem>>(`/api/orders?${params}`)
}

export function getOrder(id: number) {
  return getJson<OrderDetail>(`/api/orders/${id}`)
}

export function changeOrderStatus(id: number, status: string) {
  return sendJson<OrderDetail>(`/api/orders/${id}/status`, 'POST', { status, userId: null })
}
