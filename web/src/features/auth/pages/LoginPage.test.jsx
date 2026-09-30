import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { ApiError } from '../../../services/apiClient';
import { authValue, renderWithAuth } from '../../../test/auth';
import LoginPage from './LoginPage';

function renderLogin(auth = authValue()) {
  renderWithAuth(<LoginPage />, auth, { route: '/login' });
  return {
    auth,
    email: screen.getByLabelText('Email address'),
    password: screen.getByLabelText('Password', { selector: 'input' }),
    submit: screen.getByRole('button', { name: 'Sign in' }),
  };
}

// Form validation on the one form every user meets: validate() runs before any request,
// and a refused sign-in is told apart from an expired session.
describe('LoginPage', () => {
  it('shows both field errors and sends nothing when submitted empty', async () => {
    const { auth, submit } = renderLogin();

    await userEvent.click(submit);

    expect(screen.getByText('Email is required.')).toBeInTheDocument();
    expect(screen.getByText('Password is required.')).toBeInTheDocument();
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('refuses a malformed email and a password under 8 characters', async () => {
    const { auth, email, password, submit } = renderLogin();

    await userEvent.type(email, 'not-an-email');
    await userEvent.type(password, 'short');
    await userEvent.click(submit);

    expect(screen.getByText('Enter a valid email address.')).toBeInTheDocument();
    expect(screen.getByText('Password must be at least 8 characters.')).toBeInTheDocument();
    expect(email).toHaveAttribute('aria-invalid', 'true');
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('signs in with the trimmed email once the form is valid', async () => {
    const { auth, email, password, submit } = renderLogin();
    auth.login.mockResolvedValue({ id: 1, role: 'Reporter' });

    await userEvent.type(email, '  manager@campus.test ');
    await userEvent.type(password, 'CorrectHorse1');
    await userEvent.click(submit);

    expect(auth.login).toHaveBeenCalledWith('manager@campus.test', 'CorrectHorse1');
  });

  it('shows the error state "Could not sign in" with the API\'s reason when sign-in is refused', async () => {
    const { auth, email, password, submit } = renderLogin();
    auth.login.mockRejectedValue(new ApiError(401, 'Incorrect email or password.'));

    await userEvent.type(email, 'manager@campus.test');
    await userEvent.type(password, 'WrongPassword1');
    await userEvent.click(submit);

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not sign in. Incorrect email or password.');
  });

  it('shows "Session expired" — a different banner — after a token ran out', () => {
    renderLogin(authValue({ sessionExpired: true }));

    expect(screen.getByRole('status')).toHaveTextContent('Session expired.');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
