import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { AuthProvider } from './context/AuthContext'
import { LocaleProvider } from './context/LocaleContext'
import { ProtectedRoute } from './components/ProtectedRoute'
import { LoginPage } from './pages/LoginPage'
import { GroupsPage } from './pages/GroupsPage'
import { NewGroupPage } from './pages/NewGroupPage'
import { JoinGroupPage } from './pages/JoinGroupPage'
import { GroupDetailPage } from './pages/GroupDetailPage'
import { AddExpensePage } from './pages/AddExpensePage'
import { ProfilePage } from './pages/ProfilePage'

export default function App() {
  return (
    <BrowserRouter>
      <LocaleProvider>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route element={<ProtectedRoute />}>
              <Route path="/" element={<GroupsPage />} />
              <Route path="/profile" element={<ProfilePage />} />
              <Route path="/new-group" element={<NewGroupPage />} />
              <Route path="/join" element={<JoinGroupPage />} />
              <Route path="/groups/:groupId" element={<GroupDetailPage />} />
              <Route path="/groups/:groupId/add-expense" element={<AddExpensePage />} />
            </Route>
          </Routes>
        </AuthProvider>
      </LocaleProvider>
    </BrowserRouter>
  )
}
