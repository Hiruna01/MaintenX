import { KeyRound } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { FIELD_LIMITS, resetUserPassword } from '../services/usersApi';
import { validatePassword } from '../services/userValidation';
import styles from '../users.module.css';

/**
 * An Admin setting a temporary password for someone who has lost theirs — there is no email
 * reset in this system. Controlled inputs, `validatePassword()` mirroring ResetPasswordDto,
 * and one POST.
 */
export function ResetPasswordForm({ userId, onCancel, onDone }) {
  const [values, setValues] = useState({ password: '', confirmPassword: '' });
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  function handleChange(event) {
    const { name, value } = event.target;
    setValues((current) => ({ ...current, [name]: value }));
    setErrors((current) => ({ ...current, [name]: undefined }));
  }

  async function handleSubmit(event) {
    event.preventDefault();

    const fieldErrors = validatePassword(values.password, values.confirmPassword);
    setErrors(fieldErrors);
    setSubmitError(null);
    if (Object.keys(fieldErrors).length > 0) return;

    setIsSubmitting(true);
    try {
      await resetUserPassword(userId, values.password);
      onDone();
    } catch (caught) {
      setSubmitError(caught.message);
      setIsSubmitting(false);
    }
  }

  return (
    <form className={styles.resetForm} onSubmit={handleSubmit} noValidate>
      {submitError ? (
        <p className={form.submitError} role="alert">
          {submitError}
        </p>
      ) : null}

      <div className={form.grid}>
        <div className={form.field}>
          <label htmlFor="newPassword" className={form.label}>
            New password
          </label>
          <input
            id="newPassword"
            name="password"
            type="password"
            className={form.input}
            value={values.password}
            onChange={handleChange}
            maxLength={FIELD_LIMITS.passwordMax}
            autoComplete="new-password"
            aria-invalid={errors.password ? 'true' : 'false'}
            aria-describedby={errors.password ? 'newPassword-error' : 'newPassword-hint'}
          />
          {errors.password ? (
            <p className={form.error} id="newPassword-error">
              {errors.password}
            </p>
          ) : (
            <p className={form.hint} id="newPassword-hint">
              At least {FIELD_LIMITS.passwordMin} characters.
            </p>
          )}
        </div>

        <div className={form.field}>
          <label htmlFor="confirmNewPassword" className={form.label}>
            Confirm password
          </label>
          <input
            id="confirmNewPassword"
            name="confirmPassword"
            type="password"
            className={form.input}
            value={values.confirmPassword}
            onChange={handleChange}
            maxLength={FIELD_LIMITS.passwordMax}
            autoComplete="new-password"
            aria-invalid={errors.confirmPassword ? 'true' : 'false'}
            aria-describedby={errors.confirmPassword ? 'confirmNewPassword-error' : undefined}
          />
          {errors.confirmPassword ? (
            <p className={form.error} id="confirmNewPassword-error">
              {errors.confirmPassword}
            </p>
          ) : null}
        </div>
      </div>

      <div className={form.actions}>
        <MxButton onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </MxButton>
        <MxButton type="submit" variant="primary" icon={KeyRound} disabled={isSubmitting}>
          {isSubmitting ? 'Setting…' : 'Set password'}
        </MxButton>
      </div>
    </form>
  );
}

export default ResetPasswordForm;
