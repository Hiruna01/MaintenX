import { Navigate, Route, Routes } from 'react-router-dom';

import LoginPage from '../features/auth/pages/LoginPage';
import { MANAGER_ROLES } from '../features/auth/services/roles';
import DashboardPage from '../features/dashboard/pages/DashboardPage';
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
      </Route>

      {/* Signed in as a manager. A Reporter gets the "not authorised" page, not a blank one. */}
      <Route element={<ProtectedRoute allowedRoles={MANAGER_ROLES} />}>
        <Route path="/workflows" element={<WorkflowsPage />} />
      </Route>

      {/* Catch-all. */}
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

export default AppRoutes;
