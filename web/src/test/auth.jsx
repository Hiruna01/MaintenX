// Renders UI inside a router and a stand-in auth context, so a test can be any role without
// signing in through the API.
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';

import { AuthContext } from '../features/auth/hooks/useAuth';

export function authValue(overrides = {}) {
  const user = overrides.user === undefined ? null : overrides.user;
  return {
    user,
    role: user?.role ?? null,
    isAuthenticated: user !== null,
    isRestoring: false,
    sessionExpired: false,
    login: vi.fn(),
    logout: vi.fn(),
    clearSessionExpired: vi.fn(),
    ...overrides,
  };
}

export function signedInAs(role) {
  return authValue({ user: { id: 1, email: `${role.toLowerCase()}@campus.test`, fullName: `Demo ${role}`, role } });
}

export function renderWithAuth(ui, auth, { route = '/' } = {}) {
  return render(
    <AuthContext.Provider value={auth}>
      <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
    </AuthContext.Provider>,
  );
}
