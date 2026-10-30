import { useEffect } from 'react'

// Browser tab / screen-reader page title: "{page} · Axis", or just "Axis".
export function useDocumentTitle(title?: string) {
  useEffect(() => {
    document.title = title && title !== 'Axis' ? `${title} · Axis` : 'Axis'
  }, [title])
}
