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
