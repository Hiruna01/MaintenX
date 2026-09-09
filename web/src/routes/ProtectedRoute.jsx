import { Navigate, Outlet, useLocation } from 'react-router-dom';

import Spinner from '../components/Spinner';
import useAuth from '../features/auth/hooks/useAuth';
import { hasRole } from '../features/auth/services/roles';
import NotAuthorisedPage from './NotAuthorisedPage';

/**
 * Wraps the routes that need a signed-in user.
 *
 * The two failures are deliberately different answers, the same way the API separates 401
 * from 403: no session at all sends you to /login, while a session with the wrong role
 * renders a clear "not authorised" page. Never a blank screen.
 *
 * @param {string[]} [allowedRoles] omit for "any signed-in user".
 */
export function ProtectedRoute({ allowedRoles }) {
  const { isAuthenticated, isRestoring, role } = useAuth();
  const location = useLocation();

  if (isRestoring) {
    // A token is being checked against GET /api/auth/me. Redirecting now would sign out
    // anyone who simply reloaded the page.
    return <Spinner label="Checking your session…" />;
  }

  if (!isAuthenticated) {
    // `from` lets LoginPage send the user back where they were going.
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  if (!hasRole(role, allowedRoles)) {
    return <NotAuthorisedPage allowedRoles={allowedRoles} />;
  }

  return <Outlet />;
}

export default ProtectedRoute;
