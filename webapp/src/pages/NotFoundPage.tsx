import { Link } from 'react-router-dom'
import { useLocale } from '../context/LocaleContext'

export function NotFoundPage() {
  const { t } = useLocale()
  return (
    <main className="page">
      <h1>{t('NotFound_Title')}</h1>
      <p>{t('NotFound_Message')}</p>
      <Link className="btn btn-primary" to="/">{t('NotFound_Home')}</Link>
    </main>
  )
}
