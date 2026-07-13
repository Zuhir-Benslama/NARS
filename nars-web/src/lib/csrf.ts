/** Read CSRF token from <meta name="csrf-token"> set by the server. */
export function getCsrfToken(): string | null {
  const meta = document.querySelector<HTMLMetaElement>('meta[name="csrf-token"]')
  return meta?.content ?? null
}
