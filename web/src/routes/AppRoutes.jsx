import { Navigate, Route, Routes } from 'react-router-dom';

import AssetCreatePage from '../features/assets/pages/AssetCreatePage';
import AssetDetailPage from '../features/assets/pages/AssetDetailPage';
import AssetEditPage from '../features/assets/pages/AssetEditPage';
import AssetsPage from '../features/assets/pages/AssetsPage';
import LoginPage from '../features/auth/pages/LoginPage';
import { ADMIN_ROLES, MANAGER_ROLES } from '../features/auth/services/roles';
import DashboardPage from '../features/dashboard/pages/DashboardPage';
import WorkflowDetailPage from '../features/workflows/pages/WorkflowDetailPage';
import WorkflowsPage from '../features/workflows/pages/WorkflowsPage';
import NotFoundPage from './NotFoundPage';
import ProtectedRoute from './ProtectedRoute';

/** Every route in the app. <BrowserRouter> is one level up, in main.jsx. */
export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      {/* Signed in, any role. */}
      <Route element={<ProtectedRoute />}>
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
        <Route path="/dashboard" element={<DashboardPage />} />
        {/* Reading the registry is every role's business, as it is on the API. */}
        <Route path="/assets" element={<AssetsPage />} />
        <Route path="/assets/:id" element={<AssetDetailPage />} />
      </Route>

      {/* Changing the registry is Admin only. A Reporter is never shown these links, and
          reaching one by URL renders "not authorised" — the API would answer 403 anyway. */}
      <Route element={<ProtectedRoute allowedRoles={ADMIN_ROLES} />}>
        <Route path="/assets/new" element={<AssetCreatePage />} />
        <Route path="/assets/:id/edit" element={<AssetEditPage />} />
      </Route>

      {/* Signed in as a manager. A Reporter gets the "not authorised" page, not a blank one. */}
      <Route element={<ProtectedRoute allowedRoles={MANAGER_ROLES} />}>
        <Route path="/workflows" element={<WorkflowsPage />} />
        <Route path="/workflows/:id" element={<WorkflowDetailPage />} />
      </Route>

      {/* Catch-all. */}
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

export default AppRoutes;
