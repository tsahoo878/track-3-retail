import { test, expect, Page } from '@playwright/test'
import { EXPIRED_ORDER, PRODUCT_OVER_THRESHOLD, PRODUCT_UNDER_THRESHOLD, createPaidOrder } from './helpers'

const SKU_UNDER = 'KO-JKT-0003' // 119.00 list price, less the loyalty discount
const SKU_OVER = 'KO-JKT-0001' // 189.00 list price, less the loyalty discount

async function checkOrder(page: Page, orderNumber: string) {
  await page.goto('/returns/desk')
  await page.getByLabel('Order number').fill(orderNumber)
  await page.getByRole('button', { name: 'Check order' }).click()
}

async function actAs(page: Page, name: RegExp) {
  await page.getByLabel('Acting user').selectOption({ label: await page.getByRole('option', { name }).innerText() })
}

test.describe('RMA desk - positive', () => {
  test('staff calculates and approves a refund under 150.00', async ({ page, request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    await checkOrder(page, order.orderNumber)

    await page.getByLabel(`Return quantity for ${SKU_UNDER}`).fill('1')
    await actAs(page, /Mairi Cole \(STAFF\)/)
    await page.getByRole('button', { name: 'Calculate refund' }).click()

    const draft = page.locator('.banner.ok')
    await expect(draft).toContainText('(DRAFT)')
    await expect(draft).toContainText(order.paid.toFixed(2))

    await page.getByRole('button', { name: 'Approve' }).click()
    await expect(draft).toContainText('(APPROVED)')
    await expect(draft).toContainText('returned to stock')
  })

  test('grade C shows the reduced refund', async ({ page, request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    await checkOrder(page, order.orderNumber)

    await page.getByLabel(`Return quantity for ${SKU_UNDER}`).fill('1')
    await page.getByLabel(`Condition for ${SKU_UNDER}`).selectOption('C')
    await actAs(page, /Mairi Cole/)
    await page.getByRole('button', { name: 'Calculate refund' }).click()

    const draft = page.locator('.banner.ok')
    await expect(draft).toContainText((order.paid * 0.85).toFixed(2))
    await expect(draft).toContainText('reduced refund for condition')
  })

  test('discarding a draft issues no refund', async ({ page, request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    await checkOrder(page, order.orderNumber)

    await page.getByLabel(`Return quantity for ${SKU_UNDER}`).fill('1')
    await actAs(page, /Mairi Cole/)
    await page.getByRole('button', { name: 'Calculate refund' }).click()
    await page.getByRole('button', { name: 'Discard draft' }).click()

    await expect(page.getByRole('status')).toContainText('No refund was issued.')
  })

  test('manager approves a refund above 150.00', async ({ page, request }) => {
    const order = await createPaidOrder(request, PRODUCT_OVER_THRESHOLD, 1)
    await checkOrder(page, order.orderNumber)

    await page.getByLabel(`Return quantity for ${SKU_OVER}`).fill('1')
    await actAs(page, /Tom Achebe \(MANAGER\)/)
    await page.getByRole('button', { name: 'Calculate refund' }).click()
    await expect(page.locator('.banner.ok')).toContainText('need manager approval')

    await page.getByRole('button', { name: 'Approve' }).click()
    await expect(page.locator('.banner.ok')).toContainText('(APPROVED)')
  })
})

test.describe('RMA desk - negative', () => {
  test('shows the empty prompt before any search', async ({ page }) => {
    await page.goto('/returns/desk')
    await expect(page.getByText('Enter an order number to see what can be returned.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Check order' })).toBeDisabled()
  })

  test('unknown order shows an error', async ({ page }) => {
    await checkOrder(page, 'KO-DOES-NOT-EXIST')
    await expect(page.getByText(/was not found/i)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Calculate refund' })).toHaveCount(0)
  })

  test('order outside the return window is not returnable', async ({ page }) => {
    await checkOrder(page, EXPIRED_ORDER)
    await expect(page.getByText(/Not returnable: The \d+-day return window ended on/)).toBeVisible()
    await expect(page.getByLabel(/^Return quantity for /).first()).toBeDisabled()
  })

  test('staff cannot approve a refund above 150.00', async ({ page, request }) => {
    const order = await createPaidOrder(request, PRODUCT_OVER_THRESHOLD, 1)
    await checkOrder(page, order.orderNumber)

    await page.getByLabel(`Return quantity for ${SKU_OVER}`).fill('1')
    await actAs(page, /Mairi Cole \(STAFF\)/)
    await page.getByRole('button', { name: 'Calculate refund' }).click()
    await expect(page.locator('.banner.ok')).toContainText('Select a manager as the acting user to approve.')

    await page.getByRole('button', { name: 'Approve' }).click()
    await expect(page.getByRole('alert')).toContainText('needs approval from a manager or admin')
    await expect(page.locator('.banner.ok')).toContainText('(DRAFT)')
  })

  test('calculate stays disabled without a quantity or an acting user', async ({ page, request }) => {
    const order = await createPaidOrder(request, PRODUCT_UNDER_THRESHOLD, 1)
    await checkOrder(page, order.orderNumber)

    const calculate = page.getByRole('button', { name: 'Calculate refund' })
    await expect(calculate).toBeDisabled()
    await page.getByLabel(`Return quantity for ${SKU_UNDER}`).fill('1')
    await expect(calculate).toBeDisabled() // still no acting user
    await actAs(page, /Mairi Cole/)
    await expect(calculate).toBeEnabled()
  })
})

test.describe('Returns dashboard', () => {
  test('renders for a range with data', async ({ page }) => {
    await page.goto('/returns/dashboard')
    await expect(page.getByRole('heading').first()).toBeVisible()
  })

  test('shows the empty state for a period with no returns', async ({ page }) => {
    await page.goto('/returns/dashboard')
    const dates = page.locator('input[type="date"]')
    await dates.nth(0).fill('2020-01-01')
    await dates.nth(1).fill('2020-01-31')
    await expect(page.getByText('No data in this period.').first()).toBeVisible()
  })
})
