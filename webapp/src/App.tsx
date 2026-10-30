import { createBrowserRouter, createRoutesFromElements, Outlet, RouterProvider, Route } from 'react-router-dom'
import { AuthProvider } from './context/AuthContext'
import { LocaleProvider } from './context/LocaleContext'
import { AliasesProvider } from './context/AliasesContext'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AppLayout } from './components/AppLayout'
import { NavigationTracker } from './lib/navigation'
import { LoginPage } from './pages/LoginPage'
import { GroupsPage } from './pages/GroupsPage'
import { OverviewPage } from './pages/OverviewPage'
import { NewGroupPage } from './pages/NewGroupPage'
import { JoinGroupPage } from './pages/JoinGroupPage'
import { GroupDetailPage } from './pages/GroupDetailPage'
import { AddExpensePage } from './pages/AddExpensePage'
import { ProfilePage } from './pages/ProfilePage'
import { MembersPage } from './pages/MembersPage'
import { MemberProfilePage } from './pages/MemberProfilePage'
import { RecurringExpensesPage } from './pages/RecurringExpensesPage'
import { AddEventPage } from './pages/AddEventPage'
import { EventDetailPage } from './pages/EventDetailPage'
import { NotFoundPage } from './pages/NotFoundPage'

function AppProviders() {
  return (
    <>
      <NavigationTracker />
      <LocaleProvider>
        <AuthProvider>
          <AliasesProvider>
            <Outlet />
          </AliasesProvider>
        </AuthProvider>
      </LocaleProvider>
    </>
  )
}

const router = createBrowserRouter(createRoutesFromElements(
  <Route element={<AppProviders />}>
    <Route path="/login" element={<LoginPage />} />
    <Route element={<ProtectedRoute />}>
      <Route element={<AppLayout />}>
        <Route path="/" element={<OverviewPage />} />
        <Route path="/agenda" element={<OverviewPage agenda />} />
        <Route path="/groups" element={<GroupsPage />} />
        <Route path="/profile" element={<ProfilePage />} />
        <Route path="/new-group" element={<NewGroupPage />} />
        <Route path="/join" element={<JoinGroupPage />} />
        <Route path="/groups/:groupId" element={<GroupDetailPage />} />
        <Route path="/groups/:groupId/add-expense" element={<AddExpensePage />} />
        <Route path="/groups/:groupId/expenses/:expenseId" element={<AddExpensePage />} />
        <Route path="/groups/:groupId/members" element={<MembersPage />} />
        <Route path="/groups/:groupId/members/:memberId" element={<MemberProfilePage />} />
        <Route path="/groups/:groupId/recurring" element={<RecurringExpensesPage />} />
        <Route path="/groups/:groupId/recurring/new" element={<AddExpensePage />} />
        <Route
          path="/groups/:groupId/recurring/:recurringId"
          element={<AddExpensePage />}
        />
        <Route path="/groups/:groupId/events/new" element={<AddEventPage />} />
        <Route path="/groups/:groupId/events/:eventId" element={<EventDetailPage />} />
        <Route path="/groups/:groupId/events/:eventId/edit" element={<AddEventPage />} />
      </Route>
    </Route>
    <Route path="*" element={<NotFoundPage />} />
  </Route>,
))

export default function App() {
  return <RouterProvider router={router} />
}
