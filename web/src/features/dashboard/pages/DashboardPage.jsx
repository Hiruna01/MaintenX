import useAuth from '../../auth/hooks/useAuth';
import { roleLabel } from '../../auth/services/roles';

/** Placeholder. Real widgets arrive with the reports and work-order features. */
export function DashboardPage() {
  const { user } = useAuth();

  return (
    <section className="page">
      <h1>Dashboard</h1>
      <p className="page__lead">
        Signed in as {user.fullName} ({roleLabel(user.role)}).
      </p>
      <div className="placeholder">
        <p>Nothing here yet — this page is a placeholder.</p>
      </div>
    </section>
  );
}

export default DashboardPage;
