import type { Blocker } from 'react-router-dom'
import { useLocale } from '../context/LocaleContext'
import { Modal } from './Modal'
import './UnsavedChangesDialog.css'

export function UnsavedChangesDialog({ blocker, keepEditing, pending, open = false, onDiscard }: {
  blocker: Blocker
  keepEditing: () => void
  pending: boolean
  open?: boolean
  onDiscard?: () => void
}) {
  const { t } = useLocale()
  if (!open && blocker.state !== 'blocked') return null
  return (
    <Modal title={t('UnsavedChanges_Title')} onClose={keepEditing} dismissOnBackdrop={false}>
      <p>{t(pending ? 'UnsavedChanges_Pending' : 'UnsavedChanges_Message')}</p>
      <div className="unsaved-changes-actions">
        <button type="button" className="btn btn-primary" autoFocus onClick={keepEditing}>
          {t('UnsavedChanges_KeepEditing')}
        </button>
        <button type="button" className="btn btn-danger" disabled={pending} onClick={() => {
          if (onDiscard) onDiscard()
          else if (blocker.state === 'blocked') blocker.proceed()
        }}>
          {t('UnsavedChanges_Discard')}
        </button>
      </div>
    </Modal>
  )
}
