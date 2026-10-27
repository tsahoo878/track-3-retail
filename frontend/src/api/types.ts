export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface Product {
  id: number
  sku: string
  name: string
  categoryId: number
  categoryName: string
  brand: string
  unitPrice: number
  costPrice: number
  isActive: boolean
}

export interface Store {
  id: number
  code: string
  name: string
  region: string
  city: string
  timezone: string
  isActive: boolean
}

export interface StockLevel {
  id: number
  storeId: number
  storeName: string
  productId: number
  sku: string
  productName: string
  quantity: number
  reorderLevel: number
  updatedUtc: string
}

export interface Customer {
  id: number
  email: string
  firstName: string
  lastName: string
  loyaltyTier: string
  region: string
  createdUtc: string
}

export interface OrderListItem {
  id: number
  orderNumber: string
  customerName: string
  storeName: string
  channel: string
  status: string
  orderedUtc: string
  lineCount: number
  itemCount: number
  totalAmount: number
}

export interface OrderLine {
  id: number
  productId: number
  sku: string
  productName: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface Payment {
  id: number
  method: string
  amount: number
  cardLast4: string | null
  status: string
  paidUtc: string
}

export interface StatusHistory {
  id: number
  fromStatus: string | null
  toStatus: string
  changedUtc: string
}

export interface OrderDetail {
  id: number
  orderNumber: string
  customerName: string
  storeName: string
  channel: string
  status: string
  orderedUtc: string
  subtotal: number
  discountAmount: number
  totalAmount: number
  lines: OrderLine[]
  payments: Payment[]
  history: StatusHistory[]
}

export interface DailySales {
  date: string
  orderCount: number
  grossSales: number
  discounts: number
  netSales: number
  byChannel: { channel: string; orderCount: number; netSales: number }[]
  byStore: { storeId: number; storeName: string; orderCount: number; netSales: number }[]
}

// Returns and refunds
export interface AppUser {
  id: number
  email: string
  displayName: string
  role: string
  storeId: number | null
  isActive: boolean
}

export interface EligibilityLine {
  orderLineId: number
  sku: string
  productName: string
  categoryCode: string
  windowDays: number
  quantityOrdered: number
  quantityReturned: number
  returnableQuantity: number
  paidAmount: number
  refundedAmount: number
  returnableAmount: number
  returnableUntil: string
  eligible: boolean
  reason: string | null
}

export interface Eligibility {
  orderId: number
  orderNumber: string
  orderStatus: string
  customerName: string
  orderedUtc: string
  orderStoreId: number
  eligible: boolean
  reason: string | null
  returnableUntil: string | null
  capturedPayments: number
  refundedTotal: number
  orderRefundableRemaining: number
  serialReturner: boolean
  lines: EligibilityLine[]
}

export type ReturnGrade = 'A' | 'B' | 'C' | 'SCRAP'

export interface ReturnLineInput {
  orderLineId: number
  quantity: number
  grade: ReturnGrade
}

export interface CreateReturnRequest {
  orderNumber: string
  storeId: number
  createdByUserId: number
  lines: ReturnLineInput[]
}

export interface ReturnLine {
  id: number
  orderLineId: number
  sku: string
  productName: string
  quantity: number
  grade: string
  refundAmount: number
  restocked: boolean
  supplierClaim: boolean
}

export interface ReturnRecord {
  id: number
  rmaNumber: string
  orderId: number
  orderNumber: string
  customerName: string
  storeId: number
  storeName: string
  status: string
  refundTotal: number
  requiresManagerApproval: boolean
  createdByUserId: number
  approvedByUserId: number | null
  createdUtc: string
  approvedUtc: string | null
  serialReturner: boolean
  lines: ReturnLine[]
}

export interface ReturnRateRow {
  key: string
  label: string
  soldQuantity: number
  returnedQuantity: number
  returnRatePct: number
  refundTotal: number
}

export interface FlaggedCustomer {
  customerId: number
  customerName: string
  rmaCount: number
  distinctOrders: number
  returnedValue: number
  purchasedValue: number
  ratePct: number
}

export interface ReturnsDashboard {
  from: string
  to: string
  byProduct: ReturnRateRow[]
  byCategory: ReturnRateRow[]
  byStore: ReturnRateRow[]
  flaggedCustomers: FlaggedCustomer[]
}
