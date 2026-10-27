import { useEffect, useId, useRef, type ReactNode, type RefObject } from 'react'

export function Modal({
  title,
  onClose,
  children,
  returnFocusRef,
  dismissOnBackdrop = true,
  className = '',
}: {
  title: string
  onClose: () => void
  children: ReactNode
  returnFocusRef?: RefObject<HTMLButtonElement | null>
  dismissOnBackdrop?: boolean
  className?: string
}) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const backdropPress = useRef(false)
  const titleId = useId()

  function isOutside(clientX: number, clientY: number) {
    const rect = dialogRef.current?.getBoundingClientRect()
    return !!rect && (clientX < rect.left || clientX > rect.right ||
      clientY < rect.top || clientY > rect.bottom)
  }
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
      className={`modal-dialog card ${className}`.trim()}
      aria-labelledby={titleId}
      onCancel={(event) => {
        event.preventDefault()
        onClose()
      }}
      onPointerDown={(event) => {
        backdropPress.current = event.button === 0 && event.target === event.currentTarget &&
          isOutside(event.clientX, event.clientY)
      }}
      onPointerCancel={() => { backdropPress.current = false }}
      onClick={(event) => {
        const startedOutside = backdropPress.current
        backdropPress.current = false
        if (dismissOnBackdrop && startedOutside && event.target === event.currentTarget &&
          isOutside(event.clientX, event.clientY)) onClose()
      }}
    >
      <h3 id={titleId}>{title}</h3>
      {children}
    </dialog>
  )
}
