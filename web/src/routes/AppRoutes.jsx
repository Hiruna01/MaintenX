import { Route, Routes } from 'react-router-dom';

import AgentMonitoringPage from '../features/agents/pages/AgentMonitoringPage';
import AssetCreatePage from '../features/assets/pages/AssetCreatePage';
import AssetDetailPage from '../features/assets/pages/AssetDetailPage';
import AssetEditPage from '../features/assets/pages/AssetEditPage';
import AssetsPage from '../features/assets/pages/AssetsPage';
import LoginPage from '../features/auth/pages/LoginPage';
import {
  ADMIN_ROLES,
  DISPATCH_ROLES,
  MANAGER_ROLES,
  METRICS_ROLES,
  WORK_ORDER_ROLES,
} from '../features/auth/services/roles';
import DashboardPage from '../features/dashboard/pages/DashboardPage';
import EstatePage from '../features/estate/pages/EstatePage';
import ReportDetailPage from '../features/reports/pages/ReportDetailPage';
import ReportsPage from '../features/reports/pages/ReportsPage';
import MetricsPage from '../features/verification/pages/MetricsPage';
import VerificationDetailPage from '../features/verification/pages/VerificationDetailPage';
import VerificationsPage from '../features/verification/pages/VerificationsPage';
import UserCreatePage from '../features/users/pages/UserCreatePage';
import UserManagePage from '../features/users/pages/UserManagePage';
import UsersPage from '../features/users/pages/UsersPage';
import WorkflowDetailPage from '../features/workflows/pages/WorkflowDetailPage';
import WorkflowsPage from '../features/workflows/pages/WorkflowsPage';
import ApprovalsPage from '../features/workorders/pages/ApprovalsPage';
import WorkOrderDetailPage from '../features/workorders/pages/WorkOrderDetailPage';
import WorkOrdersPage from '../features/workorders/pages/WorkOrdersPage';
import HomeRoute from './HomeRoute';
import NotFoundPage from './NotFoundPage';
import ProtectedRoute from './ProtectedRoute';
import RolePanelGuard from './RolePanelGuard';

/** Every route in the app. <BrowserRouter> is one level up, in main.jsx. */
export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      {/* The landing page for a visitor, the dashboard for someone signed in. */}
      <Route path="/" element={<HomeRoute />} />

      {/* Signed in, any role. */}
      <Route element={<ProtectedRoute />}>
        <Route path="/dashboard" element={<DashboardPage />} />
        {/* Reading the registry is every role's business, as it is on the API. Registering and
            editing are Admin only and open as slide-overs ON TOP of the list and the detail —
            nested routes rendered into each page's <Outlet />, so the URLs still work when
            opened directly. The wrong role gets "not authorised" in the panel, never the form. */}
        <Route path="/assets" element={<AssetsPage />}>
          <Route element={<RolePanelGuard allowedRoles={ADMIN_ROLES} />}>
            <Route path="new" element={<AssetCreatePage />} />
          </Route>
        </Route>
        <Route path="/assets/:id" element={<AssetDetailPage />}>
          <Route element={<RolePanelGuard allowedRoles={ADMIN_ROLES} />}>
            <Route path="edit" element={<AssetEditPage />} />
          </Route>
        </Route>
        {/* Open to every role, like GET /api/reports/{id}: the API decides WHICH reports a
            caller may read, and a Reporter opening someone else's gets its 403 rendered. */}
        <Route path="/reports/:id" element={<ReportDetailPage />} />
        {/* Open to every role, like GET /api/verifications: the API scopes a Reporter to the
            checks on their own reports and gives a manager every one. */}
        <Route path="/verifications" element={<VerificationsPage />} />
        <Route path="/verifications/:id" element={<VerificationDetailPage />} />
      </Route>

      {/* Signed in as a manager. A Reporter gets the "not authorised" page, not a blank one. */}
      <Route element={<ProtectedRoute allowedRoles={MANAGER_ROLES} />}>
        <Route path="/reports" element={<ReportsPage />} />
        <Route path="/workflows" element={<WorkflowsPage />} />
        <Route path="/workflows/:id" element={<WorkflowDetailPage />} />
      </Route>

      {/* The dispatch board: a Technician's own queue, or the estate for a manager — the API
          decides which from the token. A Reporter has no work orders and gets "not authorised". */}
      <Route element={<ProtectedRoute allowedRoles={WORK_ORDER_ROLES} />}>
        <Route path="/workorders" element={<WorkOrdersPage />} />
        <Route path="/workorders/:id" element={<WorkOrderDetailPage />} />
      </Route>

      {/* Deciding spend is a FacilitiesManager's alone, exactly as the API's policy says — an
          Admin and a Technician reaching this by URL get "not authorised", not a 403 page. */}
      <Route element={<ProtectedRoute allowedRoles={DISPATCH_ROLES} />}>
        <Route path="/approvals" element={<ApprovalsPage />} />
      </Route>

      {/* The buildings, rooms and asset categories: Admin only, exactly the API's policy on
          their writes — the same split as registering an asset. And the user accounts. */}
      <Route element={<ProtectedRoute allowedRoles={ADMIN_ROLES} />}>
        <Route path="/estate" element={<EstatePage />} />
        {/* Accounts: Admin only, exactly the API's policy on every /api/users action but the
            technician picker. Create and manage open as slide-overs ON TOP of the list, nested
            routes rendered into its <Outlet /> — the whole route is Admin-only already, so they
            need no RolePanelGuard of their own. */}
        <Route path="/users" element={<UsersPage />}>
          <Route path="new" element={<UserCreatePage />} />
          <Route path=":id" element={<UserManagePage />} />
        </Route>
      </Route>

      {/* Estate-wide numbers: FacilitiesManager and Admin, the two roles the endpoint names. */}
      <Route element={<ProtectedRoute allowedRoles={METRICS_ROLES} />}>
        <Route path="/metrics" element={<MetricsPage />} />
        <Route path="/agent-monitoring" element={<AgentMonitoringPage />} />
      </Route>

      {/* Catch-all. */}
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

export default AppRoutes;
