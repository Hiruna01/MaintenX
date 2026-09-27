import { Outlet, useLocation } from 'react-router-dom';

import MxButton from '../components/ui/Button';
import SlideOver from '../components/ui/SlideOver';
import { ErrorState } from '../components/ui/States';
import useRoutePanel from '../components/ui/useRoutePanel';
import useAuth from '../features/auth/hooks/useAuth';
import { hasRole, roleLabel } from '../features/auth/services/roles';

/**
 * The role guard for a route that renders as a slide-over (the parent route already
 * requires a signed-in user). The wrong role gets "not authorised" in the same panel —
 * never a blank screen, and never the form. The API answers 403 to them regardless.
 */
export function RolePanelGuard({ allowedRoles }) {
  const { role } = useAuth();
  const location = useLocation();
  // Back to the page the panel sits over: /assets/new → /assets, /assets/5/edit → /assets/5.
  const panel = useRoutePanel(location.pathname.replace(/\/[^/]+\/?$/, '') || '/dashboard');

  if (hasRole(role, allowedRoles)) return <Outlet />;

  return (
    <SlideOver open={panel.open} onOpenChange={panel.onOpenChange} title="Not authorised" expandable={false}>
      <ErrorState
        title={`You are signed in as ${roleLabel(role)}`}
        message={`This needs the ${allowedRoles.map(roleLabel).join(' or ')} role.`}
        action={<MxButton onClick={() => panel.closeThen()}>Close</MxButton>}
      />
    </SlideOver>
  );
}

export default RolePanelGuard;
