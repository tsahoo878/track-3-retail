export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let message = res.statusText
    try {
      const problem = await res.json()
      message = problem.detail ?? problem.title ?? message
    } catch {
      // body was not JSON
    }
    throw new ApiError(res.status, message)
  }
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

export async function getJson<T>(url: string): Promise<T> {
  return handle<T>(await fetch(url))
}

export async function sendJson<T>(url: string, method: 'POST' | 'PUT', body: unknown): Promise<T> {
  return handle<T>(
    await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),
  )
}
