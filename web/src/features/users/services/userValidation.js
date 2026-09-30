import { FIELD_LIMITS, ROLE_OPTIONS } from './usersApi';

/** The create form's starting values. No role is chosen for the Admin — the API requires one. */
export const EMPTY_USER_VALUES = {
  fullName: '',
  email: '',
  role: '',
  password: '',
  confirmPassword: '',
};

// Deliberately loose: the API's [EmailAddress] is the rule, this only catches obvious typos.
const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+$/;

/**
 * Per-field errors for the create and edit forms, mirroring CreateUserDto / UpdateUserDto.
 * Password fields are checked only when creating — an edit never sends a password.
 *
 * @param {'create'|'edit'} mode
 * @returns {Record<string, string>} empty when the form may be sent.
 */
export function validate(values, mode) {
  const errors = {};

  const fullName = values.fullName.trim();
  if (!fullName) errors.fullName = 'Enter the person’s full name.';
  else if (fullName.length > FIELD_LIMITS.fullName) errors.fullName = `At most ${FIELD_LIMITS.fullName} characters.`;

  const email = values.email.trim();
  if (!email) errors.email = 'Enter an email address.';
  else if (email.length > FIELD_LIMITS.email) errors.email = `At most ${FIELD_LIMITS.email} characters.`;
  else if (!EMAIL_SHAPE.test(email)) errors.email = 'That does not look like an email address.';

  if (!ROLE_OPTIONS.includes(values.role)) errors.role = 'Choose a role.';

  if (mode === 'create') {
    Object.assign(errors, validatePassword(values.password, values.confirmPassword));
  }

  return errors;
}

/**
 * The password rules shared by the create form and "Reset password" — ResetPasswordDto's
 * length limits, plus the confirmation, which is the form's own check.
 */
export function validatePassword(password, confirmPassword) {
  const errors = {};

  if (!password) errors.password = 'Enter a password.';
  else if (password.length < FIELD_LIMITS.passwordMin) errors.password = `At least ${FIELD_LIMITS.passwordMin} characters.`;
  else if (password.length > FIELD_LIMITS.passwordMax) errors.password = `At most ${FIELD_LIMITS.passwordMax} characters.`;

  if (!errors.password && confirmPassword !== password) errors.confirmPassword = 'The two passwords do not match.';

  return errors;
}
