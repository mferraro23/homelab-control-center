import { useEffect, useState } from 'react'
import './App.css'

type Health = { status: string }

function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    fetch('/api/health', { signal: controller.signal })
      .then((res) => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json() as Promise<Health>
      })
      .then(setHealth)
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        setError(err instanceof Error ? err.message : String(err))
      })

    return () => controller.abort()
  }, [])

  return (
    <main>
      <h1>Homelab Control Center</h1>
      {error && <p className="status error">API unreachable: {error}</p>}
      {!error && !health && <p className="status">Checking API…</p>}
      {health && <p className="status ok">API {health.status}</p>}
    </main>
  )
}

export default App
