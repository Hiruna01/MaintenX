import { Navigate } from 'react-router-dom';

import Spinner from '../components/Spinner';
import useAuth from '../features/auth/hooks/useAuth';
import LandingPage from '../features/landing/pages/LandingPage';

/**
 * `/`: the landing page for a visitor, the dashboard for someone signed in.
 *
 * While a stored token is still being checked it waits, like ProtectedRoute: showing the
 * landing page first would flash it at every returning user before sending them on.
 */
export function HomeRoute() {
  const { isAuthenticated, isRestoring } = useAuth();

  if (isRestoring) {
    return <Spinner label="Checking your session…" />;
  }

  if (isAuthenticated) {
    return <Navigate to="/dashboard" replace />;
  }

  return <LandingPage />;
}

export default HomeRoute;
