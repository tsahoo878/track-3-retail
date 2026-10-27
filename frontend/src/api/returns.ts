import { getJson, sendJson } from './http'
import type {
  AppUser,
  CreateReturnRequest,
  Eligibility,
  ReturnRecord,
  ReturnsDashboard,
} from './types'

export function getEligibility(orderNumber: string) {
  return getJson<Eligibility>(`/api/returns/eligibility/${encodeURIComponent(orderNumber)}`)
}

export function createReturn(req: CreateReturnRequest) {
  return sendJson<ReturnRecord>('/api/returns', 'POST', req)
}

export function approveReturn(id: number, userId: number) {
  return sendJson<ReturnRecord>(`/api/returns/${id}/approve`, 'POST', { userId })
}

export function rejectReturn(id: number, userId: number) {
  return sendJson<ReturnRecord>(`/api/returns/${id}/reject`, 'POST', { userId })
}

export function getReturnsDashboard(from: string, to: string) {
  const params = new URLSearchParams({ from, to })
  return getJson<ReturnsDashboard>(`/api/returns/dashboard?${params}`)
}

export function listUsers() {
  return getJson<AppUser[]>('/api/users')
}
