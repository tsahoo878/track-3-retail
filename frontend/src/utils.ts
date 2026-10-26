export const money = (n: number) => `£${n.toFixed(2)}`

export const dateTime = (iso: string) => new Date(iso + 'Z').toLocaleString('en-GB', { timeZone: 'Europe/London' })
