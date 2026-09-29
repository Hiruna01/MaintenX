import clsx from 'clsx';
import {
  BarChart3,
  Boxes,
  Building2,
  LayoutGrid,
  LogOut,
  MessageSquareWarning,
  ShieldCheck,
  Stamp,
  Workflow,
  Wrench,
} from 'lucide-react';
import { Link, NavLink } from 'react-router-dom';

import useAuth from '../../features/auth/hooks/useAuth';
import {
  ADMIN_ROLES,
  DISPATCH_ROLES,
  MANAGER_ROLES,
  METRICS_ROLES,
  VERIFICATION_ROLES,
  WORK_ORDER_ROLES,
  hasRole,
  roleLabel,
} from '../../features/auth/services/roles';
import MxButton from '../ui/Button';
import { initials } from '../ui/format';
import BrandMark from './BrandMark';
import styles from './Shell.module.css';

/**
 * Role-based navigation — the same links and the same role lists the old top bar used,
 * grouped. `roles: null` means every signed-in user. A link a role would only be refused on
 * is never offered.
 */
const GROUPS = [
  {
    label: 'Overview',
    items: [{ to: '/dashboard', label: 'Dashboard', icon: LayoutGrid, roles: null }],
  },
  {
    label: 'Estate',
    items: [
      { to: '/assets', label: 'Assets', icon: Boxes, roles: null },
      { to: '/reports', label: 'Reports', icon: MessageSquareWarning, roles: MANAGER_ROLES },
      { to: '/estate', label: 'Buildings & rooms', icon: Building2, roles: ADMIN_ROLES },
    ],
  },
  {
    label: 'Operations',
    items: [
      { to: '/workflows', label: 'Workflows', icon: Workflow, roles: MANAGER_ROLES },
      { to: '/workorders', label: 'Work orders', icon: Wrench, roles: WORK_ORDER_ROLES },
      { to: '/approvals', label: 'Approvals', icon: Stamp, roles: DISPATCH_ROLES },
      { to: '/verifications', label: 'Verification', icon: ShieldCheck, roles: VERIFICATION_ROLES },
    ],
  },
  {
    label: 'Insight',
    items: [{ to: '/metrics', label: 'Metrics', icon: BarChart3, roles: METRICS_ROLES }],
  },
];

export function Sidebar({ open, onNavigate }) {
  const { user, role, logout } = useAuth();

  const groups = GROUPS.map((group) => ({
    ...group,
    items: group.items.filter((item) => hasRole(role, item.roles)),
  })).filter((group) => group.items.length > 0);

  return (
    <aside className={clsx(styles.sidebar, open && styles.sidebarOpen)} aria-label="Sidebar">
      <Link to="/dashboard" className={styles.brand} onClick={onNavigate}>
        <span className={styles.brandMark}>
          <BrandMark />
        </span>
        <span className={styles.brandName}>
          MaintenX
          <span className={styles.brandSub}>Campus facilities</span>
        </span>
      </Link>

      <nav className={styles.nav} aria-label="Main">
        {groups.map((group) => (
          <div key={group.label} className={styles.group}>
            <p className={styles.groupLabel}>{group.label}</p>
            {group.items.map(({ to, label, icon: Icon }) => (
              <NavLink
                key={to}
                to={to}
                onClick={onNavigate}
                className={({ isActive }) => clsx(styles.link, isActive && styles.linkActive)}
              >
                <Icon aria-hidden="true" strokeWidth={1.7} />
                {label}
              </NavLink>
            ))}
          </div>
        ))}
      </nav>

      <div className={styles.spacer} />

      <div className={styles.account}>
        <span className={styles.avatar} aria-hidden="true">
          {initials(user?.fullName)}
        </span>
        <span className={styles.accountText}>
          <span className={styles.accountName}>{user?.fullName}</span>
          <span className={styles.accountRole}>{roleLabel(user?.role)}</span>
        </span>
        <MxButton variant="ghost" size="sm" iconOnly icon={LogOut} onClick={logout} aria-label="Sign out" title="Sign out" />
      </div>
    </aside>
  );
}

export default Sidebar;
