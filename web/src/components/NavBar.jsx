import { NavLink } from 'react-router-dom';

import AuthStatus from '../features/auth/components/AuthStatus';
import useAuth from '../features/auth/hooks/useAuth';
import {
  DISPATCH_ROLES,
  MANAGER_ROLES,
  METRICS_ROLES,
  VERIFICATION_ROLES,
  WORK_ORDER_ROLES,
  hasRole,
} from '../features/auth/services/roles';

/**
 * Role-based navigation. `roles: null` means every signed-in user sees the link.
 * A Reporter never sees the manager links — the route guard would refuse them anyway,
 * but offering a link that leads to "not authorised" is a bad interface.
 *
 * Approvals is FacilitiesManager only, like the API policy behind it: a Technician never sees
 * the link at all, and neither does an Admin, whom the API would refuse. Metrics is
 * FacilitiesManager and Admin, exactly as its endpoint says — never a Reporter.
 */
const NAV_ITEMS = [
  { to: '/dashboard', label: 'Dashboard', roles: null },
  { to: '/assets', label: 'Assets', roles: null },
  { to: '/reports', label: 'Reports', roles: MANAGER_ROLES },
  { to: '/workflows', label: 'Workflows', roles: MANAGER_ROLES },
  { to: '/workorders', label: 'Work orders', roles: WORK_ORDER_ROLES },
  { to: '/approvals', label: 'Approvals', roles: DISPATCH_ROLES },
  { to: '/verifications', label: 'Verification', roles: VERIFICATION_ROLES },
  { to: '/metrics', label: 'Metrics', roles: METRICS_ROLES },
];

export function NavBar() {
  const { isAuthenticated, role } = useAuth();

  const visibleItems = isAuthenticated
    ? NAV_ITEMS.filter((item) => hasRole(role, item.roles))
    : [];

  return (
    <header className="navbar">
      <span className="navbar__brand">MaintenX</span>

      <nav className="navbar__links" aria-label="Main">
        {visibleItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) => (isActive ? 'navlink navlink--active' : 'navlink')}
          >
            {item.label}
          </NavLink>
        ))}
      </nav>

      <AuthStatus />
    </header>
  );
}

export default NavBar;
