import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { AuthProvider } from './context/AuthContext'
import { LocaleProvider } from './context/LocaleContext'
import { AliasesProvider } from './context/AliasesContext'
import { ProtectedRoute } from './components/ProtectedRoute'
import { LoginPage } from './pages/LoginPage'
import { GroupsPage } from './pages/GroupsPage'
import { NewGroupPage } from './pages/NewGroupPage'
import { JoinGroupPage } from './pages/JoinGroupPage'
import { GroupDetailPage } from './pages/GroupDetailPage'
import { AddExpensePage } from './pages/AddExpensePage'
import { ProfilePage } from './pages/ProfilePage'
import { MembersPage } from './pages/MembersPage'
import { RecurringExpensesPage } from './pages/RecurringExpensesPage'
import { AddEventPage } from './pages/AddEventPage'

export default function App() {
  return (
    <BrowserRouter>
      <LocaleProvider>
        <AuthProvider>
          <AliasesProvider>
            <Routes>
              <Route path="/login" element={<LoginPage />} />
              <Route element={<ProtectedRoute />}>
                <Route path="/" element={<GroupsPage />} />
                <Route path="/profile" element={<ProfilePage />} />
                <Route path="/new-group" element={<NewGroupPage />} />
                <Route path="/join" element={<JoinGroupPage />} />
                <Route path="/groups/:groupId" element={<GroupDetailPage />} />
                <Route path="/groups/:groupId/add-expense" element={<AddExpensePage />} />
                <Route path="/groups/:groupId/expenses/:expenseId" element={<AddExpensePage />} />
                <Route path="/groups/:groupId/members" element={<MembersPage />} />
                <Route path="/groups/:groupId/recurring" element={<RecurringExpensesPage />} />
                <Route path="/groups/:groupId/recurring/new" element={<AddExpensePage />} />
                <Route path="/groups/:groupId/recurring/:recurringId" element={<AddExpensePage />} />
                <Route path="/groups/:groupId/events/new" element={<AddEventPage />} />
                <Route path="/groups/:groupId/events/:eventId" element={<AddEventPage />} />
              </Route>
            </Routes>
          </AliasesProvider>
        </AuthProvider>
      </LocaleProvider>
    </BrowserRouter>
  )
}
