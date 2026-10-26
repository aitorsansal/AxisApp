import { useEffect, useId, useRef, type ReactNode, type RefObject } from 'react'

export function Modal({
  title,
  onClose,
  children,
  returnFocusRef,
}: {
  title: string
  onClose: () => void
  children: ReactNode
  returnFocusRef?: RefObject<HTMLButtonElement | null>
}) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  useEffect(() => {
    if (dialogRef.current && !dialogRef.current.open) dialogRef.current.showModal()
    const target = returnFocusRef?.current
    return () => {
      requestAnimationFrame(() => {
        if (target?.isConnected && !document.querySelector('dialog[open]'))
          target.focus({ preventScroll: true })
      })
    }
  }, [returnFocusRef])
  return (
    <dialog
      ref={dialogRef}
      className="modal-dialog card"
      aria-labelledby={titleId}
      onCancel={(event) => {
        event.preventDefault()
        onClose()
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          const rect = event.currentTarget.getBoundingClientRect()
          if (
            event.clientX < rect.left ||
            event.clientX > rect.right ||
            event.clientY < rect.top ||
            event.clientY > rect.bottom
          )
            onClose()
        }
      }}
    >
      <h3 id={titleId}>{title}</h3>
      {children}
    </dialog>
  )
}
