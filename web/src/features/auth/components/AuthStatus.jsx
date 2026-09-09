import Button from '../../../components/Button';
import useAuth from '../hooks/useAuth';
import { roleLabel } from '../services/roles';

/** The "signed in as … / Sign out" corner of the header. */
export function AuthStatus() {
  const { user, isAuthenticated, logout } = useAuth();

  if (!isAuthenticated) {
    return <span className="auth-status auth-status--anonymous">Not signed in</span>;
  }

  return (
    <div className="auth-status">
      <span className="auth-status__name">{user.fullName}</span>
      <span className="auth-status__role">{roleLabel(user.role)}</span>
      <Button variant="secondary" onClick={logout}>
        Sign out
      </Button>
    </div>
  );
}

export default AuthStatus;
