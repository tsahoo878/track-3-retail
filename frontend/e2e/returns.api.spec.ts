import { test, expect } from '@playwright/test'
import {
  EXPIRED_ORDER, MANAGER_ID, PRODUCT_OVER_THRESHOLD, PRODUCT_UNDER_THRESHOLD, STAFF_ID, STORE_ID,
  createDraft, createPaidOrder, discard, seedOrderNumber,
} from './helpers'

test.describe('returns API - positive', () => {
  test('eligibility lists a fresh paid order as returnable', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 2)
    const res = await request.get(`/api/returns/eligibility/${order.orderNumber}`)
    expect(res.status()).toBe(200)
    const body = await res.json()
    expect(body.eligible).toBe(true)
    expect(body.lines[0].returnableQuantity).toBe(2)
  })

  test('grade A refunds 100% and is restocked on approval', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    const created = await createDraft(request, order, 'A')
    expect(created.status()).toBe(201)
    const draft = await created.json()
    expect(draft.status).toBe('DRAFT')
    expect(draft.refundTotal).toBeCloseTo(order.paid, 2)
    expect(draft.requiresManagerApproval).toBe(false)

    const approved = await request.post(`/api/returns/${draft.id}/approve`, { data: { userId: STAFF_ID } })
    expect(approved.status()).toBe(200)
    const rma = await approved.json()
    expect(rma.status).toBe('APPROVED')
    expect(rma.lines[0].restocked).toBe(true)
  })

  test('grade C refunds 85% and is not restocked', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    const draft = await (await createDraft(request, order, 'C')).json()
    expect(draft.refundTotal).toBeCloseTo(order.paid * 0.85, 2)
    expect(draft.lines[0].restocked).toBe(false)
    await discard(request, draft.id)
  })

  test('grade SCRAP refunds in full, is not restocked and raises a supplier claim', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    const draft = await (await createDraft(request, order, 'SCRAP')).json()
    expect(draft.refundTotal).toBeCloseTo(order.paid, 2)
    expect(draft.lines[0].restocked).toBe(false)
    expect(draft.lines[0].supplierClaim).toBe(false) // set on approval, not on the draft

    const approved = await (await request.post(`/api/returns/${draft.id}/approve`, { data: { userId: STAFF_ID } })).json()
    expect(approved.lines[0].supplierClaim).toBe(true)
    expect(approved.lines[0].restocked).toBe(false)
  })

  test('refund above 150.00 can be approved by a manager', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_OVER_THRESHOLD, 1)
    const draft = await (await createDraft(request, order, 'A')).json()
    expect(draft.requiresManagerApproval).toBe(true)
    const approved = await request.post(`/api/returns/${draft.id}/approve`, { data: { userId: MANAGER_ID } })
    expect(approved.status()).toBe(200)
    expect((await approved.json()).approvedByUserId).toBe(MANAGER_ID)
  })

  test('a draft can be rejected', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    const draft = await (await createDraft(request, order, 'A')).json()
    const rejected = await request.post(`/api/returns/${draft.id}/reject`, { data: { userId: STAFF_ID } })
    expect(rejected.status()).toBe(200)
    expect((await rejected.json()).status).toBe('REJECTED')
  })

  test('dashboard returns data for a date range', async ({ request }) => {
    const res = await request.get('/api/returns/dashboard?from=2026-01-01&to=2026-12-31')
    expect(res.status()).toBe(200)
    const body = await res.json()
    expect(Array.isArray(body.byProduct)).toBe(true)
    expect(Array.isArray(body.flaggedCustomers)).toBe(true)
  })
})

test.describe('returns API - negative', () => {
  test('unknown order number returns 404', async ({ request }) => {
    const res = await request.get('/api/returns/eligibility/KO-DOES-NOT-EXIST')
    expect(res.status()).toBe(404)
  })

  test('order outside the return window is rejected with 422', async ({ request }) => {
    const elig = await (await request.get(`/api/returns/eligibility/${EXPIRED_ORDER}`)).json()
    expect(elig.eligible).toBe(false)

    const res = await request.post('/api/returns', {
      data: {
        orderNumber: EXPIRED_ORDER, storeId: STORE_ID, createdByUserId: STAFF_ID,
        lines: [{ orderLineId: elig.lines[0].orderLineId, quantity: 1, grade: 'A' }],
      },
    })
    expect(res.status()).toBe(422)
    expect(JSON.stringify(await res.json())).toMatch(/return window ended on/)
  })

  for (const status of ['PLACED', 'CANCELLED']) {
    test(`a ${status} order cannot be returned`, async ({ request }) => {
      const orderNumber = await seedOrderNumber(request, status)
      const elig = await (await request.get(`/api/returns/eligibility/${orderNumber}`)).json()
      expect(elig.eligible).toBe(false)

      const res = await request.post('/api/returns', {
        data: {
          orderNumber, storeId: STORE_ID, createdByUserId: STAFF_ID,
          lines: [{ orderLineId: elig.lines[0].orderLineId, quantity: 1, grade: 'A' }],
        },
      })
      expect(res.status()).toBe(422)
      expect(JSON.stringify(await res.json())).toContain(`A ${status} order cannot be returned.`)
    })
  }

  test('returning more than was ordered is rejected with 422', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    const res = await createDraft(request, order, 'A', 2)
    expect(res.status()).toBe(422)
    expect(JSON.stringify(await res.json())).toContain('only 1 of 1 can still be returned.')
  })

  test('returning an item that is already approved is rejected with 422', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    const draft = await (await createDraft(request, order, 'A')).json()
    await request.post(`/api/returns/${draft.id}/approve`, { data: { userId: STAFF_ID } })
    const again = await createDraft(request, order, 'A')
    expect(again.status()).toBe(422)
  })

  test('staff cannot approve a refund above 150.00', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_OVER_THRESHOLD, 1)
    const draft = await (await createDraft(request, order, 'A')).json()
    const res = await request.post(`/api/returns/${draft.id}/approve`, { data: { userId: STAFF_ID } })
    expect(res.status()).toBe(422)
    expect(JSON.stringify(await res.json())).toContain('needs approval from a manager or admin')
    await discard(request, draft.id)
  })

  test('an approved return cannot be approved or rejected again', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    const draft = await (await createDraft(request, order, 'A')).json()
    await request.post(`/api/returns/${draft.id}/approve`, { data: { userId: STAFF_ID } })

    const approveAgain = await request.post(`/api/returns/${draft.id}/approve`, { data: { userId: STAFF_ID } })
    expect(approveAgain.status()).toBe(422)
    expect(JSON.stringify(await approveAgain.json())).toContain('A APPROVED return cannot be approved.')

    const rejectAfter = await request.post(`/api/returns/${draft.id}/reject`, { data: { userId: STAFF_ID } })
    expect(rejectAfter.status()).toBe(422)
  })

  test('invalid grade and empty lines return 400', async ({ request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    const badGrade = await createDraft(request, order, 'Z')
    expect(badGrade.status()).toBe(400)

    const noLines = await request.post('/api/returns', {
      data: { orderNumber: order.orderNumber, storeId: STORE_ID, createdByUserId: STAFF_ID, lines: [] },
    })
    expect(noLines.status()).toBe(400)
  })

  test('approving an unknown return returns 404', async ({ request }) => {
    const res = await request.post('/api/returns/999999999/approve', { data: { userId: STAFF_ID } })
    expect(res.status()).toBe(404)
  })

  test('dashboard with end date before start date is rejected', async ({ request }) => {
    const res = await request.get('/api/returns/dashboard?from=2026-10-01&to=2026-09-01')
    expect(res.status()).toBe(422)
    expect(JSON.stringify(await res.json())).toContain('The end date is before the start date.')
  })
})
