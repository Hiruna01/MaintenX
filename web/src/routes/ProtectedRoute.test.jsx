import { screen } from '@testing-library/react';
import { Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { DISPATCH_ROLES, ROLES } from '../features/auth/services/roles';
import { authValue, renderWithAuth, signedInAs } from '../test/auth';
import ProtectedRoute from './ProtectedRoute';

function LoginProbe() {
  const location = useLocation();
  return <p>Login page, heading back to {location.state?.from}</p>;
}

// The approvals route, guarded exactly as AppRoutes guards it: FacilitiesManager only.
function renderApprovals(auth) {
  return renderWithAuth(
    <Routes>
      <Route path="/login" element={<LoginProbe />} />
      <Route element={<ProtectedRoute allowedRoles={DISPATCH_ROLES} />}>
        <Route path="/approvals" element={<p>Approval queue</p>} />
      </Route>
    </Routes>,
    auth,
    { route: '/approvals' },
  );
}

describe('ProtectedRoute', () => {
  it('sends a signed-out visitor to /login, remembering where they were heading', () => {
    renderApprovals(authValue());

    expect(screen.getByText('Login page, heading back to /approvals')).toBeInTheDocument();
    expect(screen.queryByText('Approval queue')).not.toBeInTheDocument();
  });

  it('shows "Not authorised" — not a blank page, not a redirect — to a signed-in user with the wrong role', () => {
    renderApprovals(signedInAs(ROLES.Technician));

    expect(screen.getByRole('heading', { name: 'Not authorised' })).toBeInTheDocument();
    expect(screen.queryByText('Approval queue')).not.toBeInTheDocument();
  });

  it('refuses an Admin too: the API policy names FacilitiesManager only, with no seniority fallback', () => {
    renderApprovals(signedInAs(ROLES.Admin));

    expect(screen.getByRole('heading', { name: 'Not authorised' })).toBeInTheDocument();
  });

  it('renders the page for the role it is for', () => {
    renderApprovals(signedInAs(ROLES.FacilitiesManager));

    expect(screen.getByText('Approval queue')).toBeInTheDocument();
  });

  it('waits on the stored-session check instead of flashing the login page', () => {
    renderApprovals(authValue({ isRestoring: true }));

    expect(screen.getByRole('status')).toHaveTextContent('Checking your session');
    expect(screen.queryByText(/Login page/)).not.toBeInTheDocument();
  });
});
