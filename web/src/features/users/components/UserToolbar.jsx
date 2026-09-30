import { Search, X } from 'lucide-react';

import MxButton from '../../../components/ui/Button';
import SelectMenu from '../../../components/ui/SelectMenu';
import { roleLabel } from '../../auth/services/roles';
import { ROLE_OPTIONS } from '../services/usersApi';
import styles from '../users.module.css';

/**
 * Search and the role picker. The search is server-side and matches the name OR the email in
 * one box, across every page — not just the one fetched.
 */
export function UserToolbar({ values, onChange, onClear }) {
  const hasFilters = Boolean(values.search || values.role);

  return (
    <div className={styles.toolbar}>
      <label className={styles.search}>
        <span className="mx-visually-hidden">Search users</span>
        <Search aria-hidden="true" />
        <input
          type="search"
          placeholder="Search by name or email"
          value={values.search}
          onChange={(event) => onChange('search', event.target.value)}
          autoComplete="off"
        />
      </label>

      <div className={styles.toolbarPickers}>
        <SelectMenu
          ariaLabel="Filter by role"
          inlineLabel="Role"
          emptyLabel="All"
          value={values.role}
          onChange={(next) => onChange('role', next)}
          options={ROLE_OPTIONS.map((role) => ({ value: role, label: roleLabel(role) }))}
        />
        {hasFilters ? (
          <MxButton variant="ghost" icon={X} onClick={onClear}>
            Clear
          </MxButton>
        ) : null}
      </div>
    </div>
  );
}

export default UserToolbar;
