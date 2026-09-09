import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';

import Button from '../../../components/Button';
import ErrorMessage from '../../../components/ErrorMessage';
import useAuth from '../hooks/useAuth';

/**
 * Returns an object of per-field messages. An empty object means the form is valid, so a
 * caller can just check `Object.keys(errors).length === 0`.
 */
function validate({ email, password }) {
  const errors = {};

  if (!email.trim()) {
    errors.email = 'Email is required.';
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
    errors.email = 'Enter a valid email address.';
  }

  if (!password) {
    errors.password = 'Password is required.';
  } else if (password.length < 8) {
    // Matches the API's [MinLength(8)] on RegisterRequest.
    errors.password = 'Password must be at least 8 characters.';
  }

  return errors;
}

export function LoginPage() {
  // Controlled inputs: React state is the single source of truth for both fields.
  const [values, setValues] = useState({ email: '', password: '' });
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const { isAuthenticated, isRestoring, sessionExpired, clearSessionExpired, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  // Where the user was heading before ProtectedRoute sent them here.
  const redirectTo = location.state?.from ?? '/dashboard';

  if (isAuthenticated) {
    return <Navigate to={redirectTo} replace />;
  }

  function handleChange(event) {
    const { name, value } = event.target;
    setValues((current) => ({ ...current, [name]: value }));
    // Clear this field's error as soon as the user starts fixing it.
    setErrors((current) => ({ ...current, [name]: undefined }));
  }

  async function handleSubmit(event) {
    event.preventDefault();

    const fieldErrors = validate(values);
    setErrors(fieldErrors);
    setSubmitError(null);

    if (Object.keys(fieldErrors).length > 0) {
      return;
    }

    setIsSubmitting(true);
    clearSessionExpired();

    try {
      await login(values.email.trim(), values.password);
      navigate(redirectTo, { replace: true });
    } catch (error) {
      setSubmitError(error.message);
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <section className="page page--narrow">
      <h1>Sign in</h1>
      <p className="page__lead">Use your MaintenX account to continue.</p>

      {sessionExpired ? (
        <ErrorMessage
          title="Session expired"
          message="Your access token is no longer valid. Please sign in again."
        />
      ) : null}

      {submitError ? <ErrorMessage title="Could not sign in" message={submitError} /> : null}

      <form className="form" onSubmit={handleSubmit} noValidate>
        <div className="form__field">
          <label htmlFor="email">Email</label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="username"
            value={values.email}
            onChange={handleChange}
            aria-invalid={errors.email ? 'true' : 'false'}
            aria-describedby={errors.email ? 'email-error' : undefined}
          />
          {errors.email ? (
            <p className="form__error" id="email-error">
              {errors.email}
            </p>
          ) : null}
        </div>

        <div className="form__field">
          <label htmlFor="password">Password</label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            value={values.password}
            onChange={handleChange}
            aria-invalid={errors.password ? 'true' : 'false'}
            aria-describedby={errors.password ? 'password-error' : undefined}
          />
          {errors.password ? (
            <p className="form__error" id="password-error">
              {errors.password}
            </p>
          ) : null}
        </div>

        <Button type="submit" disabled={isSubmitting || isRestoring}>
          {isSubmitting ? 'Signing in…' : 'Sign in'}
        </Button>
      </form>
    </section>
  );
}

export default LoginPage;
