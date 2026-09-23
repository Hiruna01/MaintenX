import { useState } from 'react';

import Button from '../../../components/Button';
import ErrorMessage from '../../../components/ErrorMessage';
import {
  ASSET_STATUSES,
  FIELD_LIMITS,
  addMonthsToDateOnly,
  enumLabel,
  isValidDateOnly,
  roomLabel,
} from '../services/assetsApi';
import { EMPTY_ASSET_VALUES, validate } from '../services/assetValidation';

/** Label + control + hint + error, wired together for screen readers. */
function Field({ id, label, error, hint, optional, children }) {
  return (
    <div className="asset-form__field">
      <label htmlFor={id}>
        {label}
        {optional ? <span className="asset-form__optional">Optional</span> : null}
      </label>
      {children}
      {error ? (
        <p className="form__error" id={`${id}-error`}>
          {error}
        </p>
      ) : hint ? (
        <p className="asset-form__hint" id={`${id}-hint`}>
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

/**
 * The create and edit form for an asset — Admin only; the routes that render it are
 * guarded, and the API answers 403 to anyone else regardless.
 *
 * Controlled inputs: React state is the single source of truth for every field.
 *
 * In edit mode the asset tag is shown but NOT editable, and it is not sent: UpdateAssetDto
 * has no field for it. The tag is the QR payload printed on a sticker already stuck to
 * the machine, and renaming the row would orphan that sticker without a trace. In create
 * mode there is no status field: a newly registered asset is Active.
 *
 * @param {'create'|'edit'} mode
 * @param {(values) => Promise<void>} onSubmit throws ApiError on failure.
 */
export function AssetForm({ mode, initialValues, categories, rooms, onSubmit, onCancel }) {
  const [values, setValues] = useState({ ...EMPTY_ASSET_VALUES, ...initialValues });
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const isCreate = mode === 'create';
  const selectedCategory = categories.find(
    (category) => String(category.id) === String(values.assetCategoryId),
  );

  function setField(name, value) {
    setValues((current) => ({ ...current, [name]: value }));
    // Clear this field's error as soon as the user starts fixing it.
    setErrors((current) => ({ ...current, [name]: undefined }));
  }

  function handleChange(event) {
    setField(event.target.name, event.target.value);
  }

  function applyCategoryWarranty() {
    setField(
      'warrantyExpiresOn',
      addMonthsToDateOnly(values.installedOn, selectedCategory.defaultWarrantyMonths),
    );
  }

  async function handleSubmit(event) {
    event.preventDefault();

    const fieldErrors = validate(values, mode);
    setErrors(fieldErrors);
    setSubmitError(null);

    if (Object.keys(fieldErrors).length > 0) return;

    setIsSubmitting(true);
    try {
      // On success the page navigates away, so there is no state to reset here.
      await onSubmit(values);
    } catch (error) {
      setIsSubmitting(false);
      if (error?.status === 409) {
        // The only conflict the API reports on an asset is a tag already in use.
        setErrors((current) => ({ ...current, assetTag: error.message }));
      } else {
        setSubmitError(error?.message ?? 'Could not save the asset.');
      }
    }
  }

  const canUseCategoryWarranty =
    isCreate && selectedCategory && isValidDateOnly(values.installedOn);

  return (
    <form className="asset-form" onSubmit={handleSubmit} noValidate>
      {submitError ? <ErrorMessage title="Could not save" message={submitError} /> : null}

      <fieldset className="asset-form__section">
        <legend>Identity</legend>
        <div className="asset-form__grid">
          {isCreate ? (
            <Field
              id="assetTag"
              label="Asset tag"
              error={errors.assetTag}
              hint="Printed on the QR sticker. Unique, and cannot be changed later."
            >
              <input
                id="assetTag"
                name="assetTag"
                className="asset-form__mono"
                value={values.assetTag}
                onChange={handleChange}
                maxLength={FIELD_LIMITS.assetTag}
                placeholder="e.g. PRJ-MAB101-01"
                autoComplete="off"
                aria-invalid={errors.assetTag ? 'true' : 'false'}
                aria-describedby={describedBy('assetTag', errors, true)}
              />
            </Field>
          ) : (
            <div className="asset-form__field">
              <span className="asset-form__static-label">Asset tag</span>
              <span className="asset-form__locked">
                <svg viewBox="0 0 16 16" aria-hidden="true">
                  <rect x="3" y="7" width="10" height="7" rx="1.5" fill="none" stroke="currentColor" strokeWidth="1.4" />
                  <path d="M5.5 7V5a2.5 2.5 0 0 1 5 0v2" fill="none" stroke="currentColor" strokeWidth="1.4" />
                </svg>
                {values.assetTag}
              </span>
              <p className="asset-form__hint">
                Not editable — the tag is the QR payload on the sticker already applied. A
                mislabelled asset gets a new sticker and a new record.
              </p>
            </div>
          )}

          <Field id="name" label="Name" error={errors.name}>
            <input
              id="name"
              name="name"
              value={values.name}
              onChange={handleChange}
              maxLength={FIELD_LIMITS.name}
              placeholder="e.g. Lecture Hall A Projector"
              aria-invalid={errors.name ? 'true' : 'false'}
              aria-describedby={describedBy('name', errors)}
            />
          </Field>
        </div>
      </fieldset>

      <fieldset className="asset-form__section">
        <legend>Classification &amp; location</legend>
        <div className="asset-form__grid">
          <Field id="assetCategoryId" label="Category" error={errors.assetCategoryId}>
            <select
              id="assetCategoryId"
              name="assetCategoryId"
              value={values.assetCategoryId}
              onChange={handleChange}
              aria-invalid={errors.assetCategoryId ? 'true' : 'false'}
              aria-describedby={describedBy('assetCategoryId', errors)}
            >
              <option value="">Choose a category…</option>
              {categories.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </Field>

          <Field id="roomId" label="Room" error={errors.roomId}>
            <select
              id="roomId"
              name="roomId"
              value={values.roomId}
              onChange={handleChange}
              aria-invalid={errors.roomId ? 'true' : 'false'}
              aria-describedby={describedBy('roomId', errors)}
            >
              <option value="">Choose a room…</option>
              {rooms.map((room) => (
                <option key={room.id} value={room.id}>
                  {roomLabel(room)}
                </option>
              ))}
            </select>
          </Field>

          {isCreate ? null : (
            <Field
              id="status"
              label="Status"
              error={errors.status}
              hint="Retired is how equipment leaves the estate. The record and its history stay."
            >
              <select
                id="status"
                name="status"
                value={values.status}
                onChange={handleChange}
                aria-invalid={errors.status ? 'true' : 'false'}
                aria-describedby={describedBy('status', errors, true)}
              >
                {ASSET_STATUSES.map((status) => (
                  <option key={status} value={status}>
                    {enumLabel(status)}
                  </option>
                ))}
              </select>
            </Field>
          )}
        </div>
      </fieldset>

      <fieldset className="asset-form__section">
        <legend>Make &amp; model</legend>
        <div className="asset-form__grid">
          <Field id="manufacturer" label="Manufacturer" error={errors.manufacturer} optional>
            <input
              id="manufacturer"
              name="manufacturer"
              value={values.manufacturer}
              onChange={handleChange}
              maxLength={FIELD_LIMITS.manufacturer}
              aria-invalid={errors.manufacturer ? 'true' : 'false'}
              aria-describedby={describedBy('manufacturer', errors)}
            />
          </Field>

          <Field id="model" label="Model" error={errors.model} optional>
            <input
              id="model"
              name="model"
              value={values.model}
              onChange={handleChange}
              maxLength={FIELD_LIMITS.model}
              aria-invalid={errors.model ? 'true' : 'false'}
              aria-describedby={describedBy('model', errors)}
            />
          </Field>
        </div>
      </fieldset>

      <fieldset className="asset-form__section">
        <legend>Dates</legend>
        <div className="asset-form__grid">
          <Field id="installedOn" label="Installed on" error={errors.installedOn}>
            <input
              id="installedOn"
              name="installedOn"
              type="date"
              value={values.installedOn}
              onChange={handleChange}
              aria-invalid={errors.installedOn ? 'true' : 'false'}
              aria-describedby={describedBy('installedOn', errors)}
            />
          </Field>

          <Field
            id="warrantyExpiresOn"
            label="Warranty expires on"
            error={errors.warrantyExpiresOn}
            hint="Leave blank if no warranty is recorded."
            optional
          >
            <input
              id="warrantyExpiresOn"
              name="warrantyExpiresOn"
              type="date"
              value={values.warrantyExpiresOn}
              onChange={handleChange}
              aria-invalid={errors.warrantyExpiresOn ? 'true' : 'false'}
              aria-describedby={describedBy('warrantyExpiresOn', errors, true)}
            />
            {canUseCategoryWarranty ? (
              <button type="button" className="asset-form__suggest" onClick={applyCategoryWarranty}>
                Use the {selectedCategory.name} default ({selectedCategory.defaultWarrantyMonths}{' '}
                months from installation)
              </button>
            ) : null}
          </Field>
        </div>
      </fieldset>

      <div className="asset-form__actions">
        <Button variant="secondary" onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </Button>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : isCreate ? 'Register asset' : 'Save changes'}
        </Button>
      </div>
    </form>
  );
}

export default AssetForm;
