import { useMemo } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import { formatInstant } from '../../../components/ui/format';
import Notice from '../../../components/ui/Notice';
import { StatusPill } from '../../../components/ui/Pill';
import Skeleton from '../../../components/ui/Skeleton';
import { ErrorState } from '../../../components/ui/States';
import useRoutePanel from '../../../components/ui/useRoutePanel';
import useAuth from '../../auth/hooks/useAuth';
import { roleLabel } from '../../auth/services/roles';
import AccountActions from '../components/AccountActions';
import AccountStatusPill from '../components/AccountStatusPill';
import UserForm from '../components/UserForm';
import UserSheet from '../components/UserSheet';
import useUser from '../hooks/useUser';
import { updateUser } from '../services/usersApi';
import styles from '../users.module.css';

/** The API's UserAdminDto, as the form's string-valued fields. */
function toFormValues(user) {
  return { fullName: user.fullName, email: user.email, role: user.role };
}

/**
 * One account's panel body: what it is, the edit form, and the account switches. Keyed by the
 * page on every change, so it remounts and reads the account again — there is no refetch in
 * useFetch.
 */
function ManageBody({ id, currentUserId, notice, onSaved, onChanged, onCancel, onDirtyChange, onClose }) {
  const { data: user, isLoading, error } = useUser(id);
  const initialValues = useMemo(() => (user ? toFormValues(user) : null), [user]);

  if (isLoading) {
    return (
      <div className={styles.stack} role="status" aria-label="Loading">
        <Skeleton height={64} radius={14} />
        <Skeleton height={42} radius={10} />
        <Skeleton height={42} radius={10} />
        <Skeleton height={42} radius={10} />
      </div>
    );
  }

  if (error) {
    return (
      <ErrorState
        title={error.status === 404 ? 'Account not found' : 'Could not load this account'}
        message={error.message}
        action={<MxButton onClick={onClose}>Close</MxButton>}
      />
    );
  }

  const isSelf = user.id === currentUserId;

  return (
    <div className={styles.stack}>
      {notice ? <Notice>{notice}</Notice> : null}

      <div className={styles.summary}>
        <div className={styles.summaryPills}>
          <StatusPill status={user.role} label={roleLabel(user.role)} />
          <AccountStatusPill isActive={user.isActive} />
          {isSelf ? <span className={styles.youTag}>You</span> : null}
        </div>
        <p className={styles.sectionNote}>
          Account #{user.id} · created {formatInstant(user.createdAt)} · last changed {formatInstant(user.updatedAt)}
        </p>
      </div>

      <UserForm
        mode="edit"
        initialValues={initialValues}
        roleLocked={isSelf}
        onSubmit={(values) => onSaved(user, values)}
        onCancel={onCancel}
        onDirtyChange={onDirtyChange}
      />

      <AccountActions user={user} isSelf={isSelf} onChanged={onChanged} />
    </div>
  );
}

/**
 * Manage an account — a slide-over on top of the user list (/users/:id). Admin only, like the
 * whole /users route. Edit, deactivate or reactivate, reset the password.
 *
 * After any change the page navigates to its own URL with a fresh `state.refresh`: the list
 * behind remounts on that key, and so does this panel's body, while the sheet itself stays
 * open. Saving the form slides the panel away instead.
 */
export function UserManagePage() {
  const { id } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const panel = useRoutePanel('/users');
  const { user: currentUser } = useAuth();

  const refresh = location.state?.refresh ?? 0;
  const notice = location.state?.notice ?? null;

  async function handleSaved(user, values) {
    await updateUser(user.id, values);
    panel.closeThen('/users', { state: { refresh: Date.now() } });
  }

  function handleChanged(message) {
    navigate(`/users/${id}`, { replace: true, state: { refresh: Date.now(), notice: message } });
  }

  return (
    <UserSheet panel={panel} title="Manage account" description="Edit the person, switch the account off or on, or set a new password.">
      {({ onDirtyChange, onCancel }) => (
        <ManageBody
          key={`${id}-${refresh}`}
          id={id}
          currentUserId={currentUser?.id}
          notice={notice}
          onSaved={handleSaved}
          onChanged={handleChanged}
          onCancel={onCancel}
          onDirtyChange={onDirtyChange}
          onClose={() => panel.closeThen()}
        />
      )}
    </UserSheet>
  );
}

export default UserManagePage;
