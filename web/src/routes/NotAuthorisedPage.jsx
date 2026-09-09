import { Link } from 'react-router-dom';

import useAuth from '../features/auth/hooks/useAuth';
import { roleLabel } from '../features/auth/services/roles';

/** The 403 equivalent: we know who you are, and this page is not for you. */
export function NotAuthorisedPage({ allowedRoles }) {
  const { role } = useAuth();

  return (
    <section className="page page--narrow">
      <h1>Not authorised</h1>
      <p className="page__lead">
        You are signed in as <strong>{roleLabel(role)}</strong>, and that role cannot open
        this page.
      </p>
      {allowedRoles?.length ? (
        <p>Required role: {allowedRoles.map(roleLabel).join(' or ')}.</p>
      ) : null}
      <Link to="/dashboard">Back to the dashboard</Link>
    </section>
  );
}

export default NotAuthorisedPage;
