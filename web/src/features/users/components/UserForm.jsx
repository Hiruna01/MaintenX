import clsx from 'clsx';
import { Lock } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import SelectMenu from '../../../components/ui/SelectMenu';
import { roleLabel } from '../../auth/services/roles';
import { FIELD_LIMITS, ROLE_OPTIONS } from '../services/usersApi';
import { EMPTY_USER_VALUES, validate } from '../services/userValidation';
import styles from '../users.module.css';

/** Label + control + hint + error, wired together for screen readers. */
function Field({ id, label, error, hint, wide = false, children }) {
  return (
    <div className={clsx(form.field, wide && form.wide)}>
      <label htmlFor={id} className={form.label}>
        {label}
      </label>
      {children}
      {error ? (
        <p className={form.error} id={`${id}-error`}>
          {error}
        </p>
      ) : hint ? (
        <p className={form.hint} id={`${id}-hint`}>
          {hint}
        </p>
      ) : null}
    </div>
  );
}

function describedBy(id, errors, hint) {
  if (errors[id]) return `${id}-error`;
  return hint ? `${id}-hint` : undefined;
}

const ROLE_HINTS = {
  Reporter: 'Reports faults and confirms repairs, from the phone.',
  Technician: 'Sees and completes the jobs assigned to them.',
  FacilitiesManager: 'Approves spend, assigns technicians and books visits.',
  Admin: 'Manages the registry, the estate and these accounts.',
};

/**
 * The create and edit form for an account, laid out for the slide-over. Admin only — the route
 * is guarded, and the API answers 403 to anyone else regardless.
 *
 * Controlled inputs, and `validate()` from userValidation.js mirroring the DTOs. Creating
 * asks for a password (and its confirmation); editing never sends one — resetting a password
 * is its own action on the manage panel.
 *
 * `roleLocked` is set on the Admin's OWN account: the API refuses an Admin changing their own
 * role (409), so the control is shown locked instead of offered and refused.
 *
 * @param {'create'|'edit'} mode
 * @param {(values) => Promise<void>} onSubmit throws ApiError on failure.
 * @param {(dirty: boolean) => void} [onDirtyChange] told whenever the form gains or loses edits.
 */
export function UserForm({ mode, initialValues, roleLocked = false, onSubmit, onCancel, onDirtyChange }) {
  const startValues = useMemo(() => ({ ...EMPTY_USER_VALUES, ...initialValues }), [initialValues]);
  const [values, setValues] = useState(startValues);
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const isCreate = mode === 'create';

  const isDirty = useMemo(
    () => Object.keys(startValues).some((key) => String(values[key] ?? '') !== String(startValues[key] ?? '')),
    [values, startValues],
  );

  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  function setField(name, value) {
    setValues((current) => ({ ...current, [name]: value }));
    setErrors((current) => ({ ...current, [name]: undefined }));
  }

  function handleChange(event) {
    setField(event.target.name, event.target.value);
  }

  async function handleSubmit(event) {
    event.preventDefault();

    const fieldErrors = validate(values, mode);
    setErrors(fieldErrors);
    setSubmitError(null);

    if (Object.keys(fieldErrors).length > 0) return;

    setIsSubmitting(true);
    try {
      // On success the panel navigates away, so there is no state to reset here.
      await onSubmit(values);
    } catch (error) {
      setIsSubmitting(false);
      if (isCreate && error?.status === 409) {
        // The only conflict creating an account can hit is an email already in use.
        setErrors((current) => ({ ...current, email: error.message }));
      } else {
        // An edit's 409 may be the email, the Admin's own role, or a Technician's live work —
        // shown exactly as the API worded it.
        setSubmitError(error?.message ?? 'Could not save the account.');
      }
    }
  }

  return (
    <form className={styles.form} onSubmit={handleSubmit} noValidate>
      {submitError ? (
        <p className={form.submitError} role="alert">
          {submitError}
        </p>
      ) : null}

      <fieldset className={styles.formSection}>
        <legend className={styles.formLegend}>Person</legend>
        <div className={form.grid}>
          <Field id="fullName" label="Full name" error={errors.fullName} wide>
            <input
              id="fullName"
              name="fullName"
              className={form.input}
              value={values.fullName}
              onChange={handleChange}
              maxLength={FIELD_LIMITS.fullName}
              autoComplete="off"
              aria-invalid={errors.fullName ? 'true' : 'false'}
              aria-describedby={describedBy('fullName', errors)}
            />
          </Field>

          <Field
            id="email"
            label="Email"
            error={errors.email}
            hint="What they sign in with. Stored in lower case."
            wide
          >
            <input
              id="email"
              name="email"
              type="email"
              className={form.input}
              value={values.email}
              onChange={handleChange}
              maxLength={FIELD_LIMITS.email}
              placeholder="name@campus.test"
              autoComplete="off"
              spellCheck={false}
              aria-invalid={errors.email ? 'true' : 'false'}
              aria-describedby={describedBy('email', errors, true)}
            />
          </Field>
        </div>
      </fieldset>

      <fieldset className={styles.formSection}>
        <legend className={styles.formLegend}>Role</legend>
        {roleLocked ? (
          <div className={form.field}>
            <span className={styles.lockedValue}>
              <Lock aria-hidden="true" />
              {roleLabel(values.role)}
            </span>
            <p className={form.hint}>
              This is your own account. Another Admin has to change your role — that is what keeps at least one
              Admin able to sign in.
            </p>
          </div>
        ) : (
          <Field
            id="role"
            label="Role"
            error={errors.role}
            hint={ROLE_HINTS[values.role] ?? 'Decides what this person can see and do.'}
          >
            <SelectMenu
              id="role"
              block
              placeholder="Choose a role…"
              value={values.role}
              onChange={(next) => setField('role', next)}
              options={ROLE_OPTIONS.map((role) => ({ value: role, label: roleLabel(role) }))}
              invalid={Boolean(errors.role)}
              describedBy={describedBy('role', errors, true)}
            />
          </Field>
        )}
      </fieldset>

      {isCreate ? (
        <fieldset className={styles.formSection}>
          <legend className={styles.formLegend}>First password</legend>
          <div className={form.grid}>
            <Field id="password" label="Password" error={errors.password} hint={`At least ${FIELD_LIMITS.passwordMin} characters.`}>
              <input
                id="password"
                name="password"
                type="password"
                className={form.input}
                value={values.password}
                onChange={handleChange}
                maxLength={FIELD_LIMITS.passwordMax}
                autoComplete="new-password"
                aria-invalid={errors.password ? 'true' : 'false'}
                aria-describedby={describedBy('password', errors, true)}
              />
            </Field>
            <Field id="confirmPassword" label="Confirm password" error={errors.confirmPassword}>
              <input
                id="confirmPassword"
                name="confirmPassword"
                type="password"
                className={form.input}
                value={values.confirmPassword}
                onChange={handleChange}
                maxLength={FIELD_LIMITS.passwordMax}
                autoComplete="new-password"
                aria-invalid={errors.confirmPassword ? 'true' : 'false'}
                aria-describedby={describedBy('confirmPassword', errors)}
              />
            </Field>
          </div>
          <p className={styles.sectionNote}>Give it to them in person. They sign in with it straight away.</p>
        </fieldset>
      ) : null}

      <div className={styles.formFooter}>
        <span className={styles.formFooterNote}>
          {isDirty ? 'Unsaved changes' : isCreate ? 'Starts out active' : 'No changes yet'}
        </span>
        <MxButton onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </MxButton>
        <MxButton type="submit" variant="primary" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : isCreate ? 'Create account' : 'Save changes'}
        </MxButton>
      </div>
    </form>
  );
}

export default UserForm;
