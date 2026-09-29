import { useLocation } from 'react-router-dom';

import AppShell from './components/shell/AppShell';
import useAuth from './features/auth/hooks/useAuth';
import AppRoutes from './routes/AppRoutes';

// Signed out, these fill the screen on their own canvas: the landing page and the login page.
const FULL_SCREEN_ROUTES = ['/', '/login'];

/**
 * Signed in, every page sits inside the sidebar shell. Signed out, the landing page (`/`) and the
 * login page fill the screen on their own canvas; anything else signed out (a 404) renders in
 * the plain container.
 */
export function App() {
  const { isAuthenticated } = useAuth();
  const location = useLocation();

  if (!isAuthenticated && FULL_SCREEN_ROUTES.includes(location.pathname)) {
    return (
      <div className="mx-app">
        <AppRoutes />
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="app">
        <main className="app__main">
          <AppRoutes />
        </main>
      </div>
    );
  }

  return (
    <AppShell>
      <AppRoutes />
    </AppShell>
  );
}

export default App;
