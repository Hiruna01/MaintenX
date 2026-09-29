import { AlarmClock, ArrowRight, CircleAlert, Eye, EyeOff, Lock, Mail, QrCode, Scale, ShieldCheck } from 'lucide-react';
import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import BrandMark from '../../../components/shell/BrandMark';
import LoopIllustration from '../components/LoopIllustration';
import useAuth from '../hooks/useAuth';
import styles from '../login.module.css';

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

const FEATURES = [
  {
    icon: QrCode,
    title: 'Scan a sticker, report a fault',
    body: 'Every machine carries a QR label. A photo and a sentence from the phone is a report.',
  },
  {
    icon: Scale,
    title: 'Agents advise, people decide',
    body: 'Agents read the history and propose a fix. Spending above the threshold waits for a manager.',
  },
  {
    icon: ShieldCheck,
    title: 'Every repair is checked',
    body: 'Days after the job, the reporter is asked whether it held. A fault that returns goes round again.',
  },
];

/**
 * Sign in. The form, its validate() and the redirect back to where the user was heading are
 * unchanged; the page around them is the redesigned split layout.
 *
 * Two banners, two different facts: "session expired" is the API refusing a token that was
 * good once (a 401 on a request that carried one), "could not sign in" is this attempt failing.
 * There is no sign-up here — accounts are issued, not self-registered.
 */
export function LoginPage() {
  // Controlled inputs: React state is the single source of truth for both fields.
  const [values, setValues] = useState({ email: '', password: '' });
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [showPassword, setShowPassword] = useState(false);

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
    <div className={styles.screen}>
      <div className={styles.frame}>
        <main className={styles.formSide}>
          <div className={styles.formInner}>
            <div className={styles.brand}>
              <span className={styles.brandMark}>
                <BrandMark />
              </span>
              <span className={styles.brandName}>MaintenX</span>
            </div>

            <h1 className={styles.title}>Sign in</h1>
            <p className={styles.lead}>Campus facilities, from a reported fault to a repair that held.</p>

            {sessionExpired ? (
              <p className={`${styles.banner} ${styles.bannerWarn}`} role="status">
                <AlarmClock aria-hidden="true" />
                <span>
                  <strong>Session expired.</strong> Your access token is no longer valid. Please sign in again.
                </span>
              </p>
            ) : null}

            {submitError ? (
              <p className={`${styles.banner} ${styles.bannerError}`} role="alert">
                <CircleAlert aria-hidden="true" />
                <span>
                  <strong>Could not sign in.</strong> {submitError}
                </span>
              </p>
            ) : null}

            <form className={styles.form} onSubmit={handleSubmit} noValidate>
              <div className={styles.field}>
                <label htmlFor="email" className={styles.label}>
                  Email address
                </label>
                <div className={styles.control}>
                  <Mail aria-hidden="true" className={styles.controlIcon} />
                  <input
                    id="email"
                    name="email"
                    type="email"
                    className={styles.input}
                    autoComplete="username"
                    placeholder="name@campus.test"
                    value={values.email}
                    onChange={handleChange}
                    aria-invalid={errors.email ? 'true' : 'false'}
                    aria-describedby={errors.email ? 'email-error' : undefined}
                    autoFocus
                  />
                </div>
                {errors.email ? (
                  <p className={styles.error} id="email-error">
                    {errors.email}
                  </p>
                ) : null}
              </div>

              <div className={styles.field}>
                <label htmlFor="password" className={styles.label}>
                  Password
                </label>
                <div className={styles.control}>
                  <Lock aria-hidden="true" className={styles.controlIcon} />
                  <input
                    id="password"
                    name="password"
                    type={showPassword ? 'text' : 'password'}
                    className={styles.input}
                    autoComplete="current-password"
                    placeholder="At least 8 characters"
                    value={values.password}
                    onChange={handleChange}
                    aria-invalid={errors.password ? 'true' : 'false'}
                    aria-describedby={errors.password ? 'password-error' : undefined}
                  />
                  <button
                    type="button"
                    className={styles.reveal}
                    onClick={() => setShowPassword((current) => !current)}
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    aria-pressed={showPassword}
                  >
                    {showPassword ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
                  </button>
                </div>
                {errors.password ? (
                  <p className={styles.error} id="password-error">
                    {errors.password}
                  </p>
                ) : null}
              </div>

              <MxButton type="submit" variant="primary" className={styles.submit} disabled={isSubmitting || isRestoring}>
                {isSubmitting ? 'Signing in…' : 'Sign in'}
                {isSubmitting ? null : <ArrowRight aria-hidden="true" />}
              </MxButton>
            </form>

            <p className={styles.note}>Accounts are issued by your facilities admin. There is no public sign-up.</p>
          </div>
        </main>

        <aside className={styles.storySide} aria-label="About MaintenX">
          <div className={styles.illustration}>
            <LoopIllustration />
          </div>

          <div className={styles.story}>
            <h2 className={styles.storyTitle}>
              <span>MaintenX runs campus repairs.</span> From a sticker to a fix that held.
            </h2>

            <ul className={styles.features}>
              {FEATURES.map(({ icon: Icon, title, body }, index) => (
                <li key={title} className={styles.feature} style={{ animationDelay: `${180 + index * 80}ms` }}>
                  <span className={styles.featureIcon} aria-hidden="true">
                    <Icon strokeWidth={1.8} />
                  </span>
                  <span>
                    <span className={styles.featureTitle}>{title}</span>
                    <span className={styles.featureBody}>{body}</span>
                  </span>
                </li>
              ))}
            </ul>
          </div>

          <p className={styles.footer}>SE3090 · Campus facilities</p>
        </aside>
      </div>
    </div>
  );
}

export default LoginPage;
