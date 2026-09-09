import { NavLink } from 'react-router-dom';

import AuthStatus from '../features/auth/components/AuthStatus';
import useAuth from '../features/auth/hooks/useAuth';
import { MANAGER_ROLES, hasRole } from '../features/auth/services/roles';

/**
 * Role-based navigation. `roles: null` means every signed-in user sees the link.
 * A Reporter never sees the manager links — the route guard would refuse them anyway,
 * but offering a link that leads to "not authorised" is a bad interface.
 */
const NAV_ITEMS = [
  { to: '/dashboard', label: 'Dashboard', roles: null },
  { to: '/workflows', label: 'Workflows', roles: MANAGER_ROLES },
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
