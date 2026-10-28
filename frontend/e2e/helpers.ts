import { APIRequestContext, expect } from '@playwright/test'

// Seed users (backend/db/migrations/0002_seed_reference.sql)
export const MANAGER_ID = 2 // Tom Achebe
export const STAFF_ID = 5 // Mairi Cole
export const STORE_ID = 1

// Seed products (0003_seed_catalogue_stock.sql)
export const PRODUCT_UNDER_THRESHOLD = 3 // Cairn Packable Jacket, 119.00
export const PRODUCT_OVER_THRESHOLD = 1 // Ridgeway Shell Jacket, 189.00 (> 150.00 manager threshold)

export interface PaidOrder {
  id: number
  orderNumber: string
  paid: number // total after the customer's loyalty discount; refunds are capped to this
  lines: { id: number; sku: string; quantity: number }[]
}

// Creates a fresh PAID order dated now, so it is always inside the return window.
// Stock is topped up first because each run consumes stock.
export async function createPaidOrder(request: APIRequestContext, productId: number, quantity: number): Promise<PaidOrder> {
  const adjust = await request.post('/api/stock/adjust', {
    data: { storeId: STORE_ID, productId, delta: quantity, reason: 'e2e top-up' },
  })
  expect(adjust.ok()).toBeTruthy()

  const created = await request.post('/api/orders', {
    data: { customerId: 1, storeId: STORE_ID, channel: 'STORE', lines: [{ productId, quantity }] },
  })
  expect(created.status()).toBe(201)
  const order = await created.json()

  const pay = await request.post(`/api/orders/${order.id}/payments`, {
    data: { method: 'CASH', amount: order.totalAmount, cardLast4: null, providerRef: `E2E-${Date.now()}` },
  })
  expect(pay.status()).toBe(201) // a full payment moves the order from PLACED to PAID

  return {
    id: order.id,
    orderNumber: order.orderNumber,
    paid: order.totalAmount,
    lines: order.lines.map((l: any) => ({ id: l.id, sku: l.sku, quantity: l.quantity })),
  }
}

export async function createDraft(
  request: APIRequestContext,
  order: PaidOrder,
  grade: string,
  quantity = 1,
  lineIndex = 0,
) {
  return request.post('/api/returns', {
    data: {
      orderNumber: order.orderNumber,
      storeId: STORE_ID,
      createdByUserId: STAFF_ID,
      lines: [{ orderLineId: order.lines[lineIndex].id, quantity, grade }],
    },
  })
}

// Frees the quantity held by a draft. Safe to call on non-drafts (the 422 is ignored).
export async function discard(request: APIRequestContext, rmaId: number) {
  await request.post(`/api/returns/${rmaId}/reject`, { data: { userId: STAFF_ID } })
}

// Finds the order number of a seed order with the given status.
export async function seedOrderNumber(request: APIRequestContext, status: string): Promise<string> {
  const res = await request.get(`/api/orders?status=${status}&pageSize=1`)
  expect(res.ok()).toBeTruthy()
  const body = await res.json()
  expect(body.items.length).toBeGreaterThan(0)
  return body.items[0].orderNumber
}

// FULFILLED seed order from 2026-08-01, outside every return window (30 or 60 days).
export const EXPIRED_ORDER = 'KO-20260801-0002'
