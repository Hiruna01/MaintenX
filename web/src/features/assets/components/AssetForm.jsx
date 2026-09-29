import clsx from 'clsx';
import { Lock, Sparkles } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';

import MxButton from '../../../components/ui/Button';
import Segmented from '../../../components/ui/Segmented';
import SelectMenu from '../../../components/ui/SelectMenu';
import { ErrorState } from '../../../components/ui/States';
import {
  ASSET_STATUSES,
  FIELD_LIMITS,
  addMonthsToDateOnly,
  enumLabel,
  isValidDateOnly,
  roomLabel,
} from '../services/assetsApi';
import { EMPTY_ASSET_VALUES, validate } from '../services/assetValidation';
import styles from '../assets.module.css';
import AssetLabel from './AssetLabel';

/** Label + control + hint + error, wired together for screen readers. */
function Field({ id, label, error, hint, optional, children, wide = false }) {
  return (
    <div className={clsx(styles.field, wide && styles.fieldWide)}>
      <label htmlFor={id} className={styles.fieldLabel}>
        {label}
        {optional ? <span className={styles.fieldOptional}>Optional</span> : null}
      </label>
      {children}
      {error ? (
        <p className={styles.fieldError} id={`${id}-error`}>
          {error}
        </p>
      ) : hint ? (
        <p className={styles.fieldHint} id={`${id}-hint`}>
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

function Section({ title, children }) {
  return (
    <fieldset className={styles.formSection}>
      <legend className={styles.formLegend}>{title}</legend>
      <div className={styles.formGrid}>{children}</div>
    </fieldset>
  );
}

/**
 * The register and edit form for an asset, laid out for the slide-over — Admin only; the
 * routes that render it are guarded, and the API answers 403 to anyone else regardless.
 *
 * Controlled inputs: React state is the single source of truth for every field.
 *
 * In edit mode the asset tag is shown but NOT editable, and it is not sent: UpdateAssetDto
 * has no field for it. The tag is the QR payload printed on a sticker already stuck to the
 * machine, and renaming the row would orphan that sticker without a trace. In create mode
 * there is no status field: a newly registered asset is Active.
 *
 * The label at the top is a live preview of the sticker the tag becomes.
 *
 * @param {'create'|'edit'} mode
 * @param {(values) => Promise<void>} onSubmit throws ApiError on failure.
 * @param {(dirty: boolean) => void} [onDirtyChange] told whenever the form gains or loses edits.
 */
export function AssetForm({ mode, initialValues, categories, rooms, onSubmit, onCancel, onDirtyChange }) {
  const startValues = useMemo(() => ({ ...EMPTY_ASSET_VALUES, ...initialValues }), [initialValues]);
  const [values, setValues] = useState(startValues);
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const isCreate = mode === 'create';
  const selectedCategory = categories.find((category) => String(category.id) === String(values.assetCategoryId));
  const selectedRoom = rooms.find((room) => String(room.id) === String(values.roomId));

  const isDirty = useMemo(
    () => Object.keys(startValues).some((key) => String(values[key] ?? '') !== String(startValues[key] ?? '')),
    [values, startValues],
  );

  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  function setField(name, value) {
    setValues((current) => ({ ...current, [name]: value }));
    // Clear this field's error as soon as the user starts fixing it.
    setErrors((current) => ({ ...current, [name]: undefined }));
  }

  function handleChange(event) {
    setField(event.target.name, event.target.value);
  }

  function applyCategoryWarranty() {
    setField('warrantyExpiresOn', addMonthsToDateOnly(values.installedOn, selectedCategory.defaultWarrantyMonths));
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
      if (error?.status === 409) {
        // The only conflict the API reports on an asset is a tag already in use.
        setErrors((current) => ({ ...current, assetTag: error.message }));
      } else {
        setSubmitError(error?.message ?? 'Could not save the asset.');
      }
    }
  }

  const canUseCategoryWarranty = isCreate && selectedCategory && isValidDateOnly(values.installedOn);

  return (
    <form className={styles.form} onSubmit={handleSubmit} noValidate>
      <AssetLabel
        tag={values.assetTag}
        name={values.name}
        location={selectedRoom ? roomLabel(selectedRoom) : 'Room not chosen yet'}
        size="compact"
        locked={!isCreate}
      />

      {submitError ? <ErrorState compact title="Could not save the asset" message={submitError} /> : null}

      <Section title="Identity">
        {isCreate ? (
          <Field
            id="assetTag"
            label="Asset tag"
            error={errors.assetTag}
            hint="Printed on the QR sticker. Unique, and cannot be changed later."
            wide
          >
            <input
              id="assetTag"
              name="assetTag"
              className={clsx(styles.input, styles.inputMono)}
              value={values.assetTag}
              onChange={handleChange}
              maxLength={FIELD_LIMITS.assetTag}
              placeholder="e.g. PRJ-MAB101-01"
              autoComplete="off"
              spellCheck={false}
              aria-invalid={errors.assetTag ? 'true' : 'false'}
              aria-describedby={describedBy('assetTag', errors, true)}
            />
          </Field>
        ) : (
          <div className={clsx(styles.field, styles.fieldWide)}>
            <span className={styles.fieldLabel}>Asset tag</span>
            <span className={styles.lockedValue}>
              <Lock aria-hidden="true" />
              <span className="mx-mono">{values.assetTag}</span>
            </span>
            <p className={styles.fieldHint}>
              Not editable — the tag is the QR payload on the sticker already applied. A mislabelled
              asset gets a new sticker and a new record.
            </p>
          </div>
        )}

        <Field id="name" label="Name" error={errors.name} wide>
          <input
            id="name"
            name="name"
            className={styles.input}
            value={values.name}
            onChange={handleChange}
            maxLength={FIELD_LIMITS.name}
            placeholder="e.g. Lecture Hall A projector"
            aria-invalid={errors.name ? 'true' : 'false'}
            aria-describedby={describedBy('name', errors)}
          />
        </Field>

        <Field id="manufacturer" label="Manufacturer" error={errors.manufacturer} optional>
          <input
            id="manufacturer"
            name="manufacturer"
            className={styles.input}
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
            className={styles.input}
            value={values.model}
            onChange={handleChange}
            maxLength={FIELD_LIMITS.model}
            aria-invalid={errors.model ? 'true' : 'false'}
            aria-describedby={describedBy('model', errors)}
          />
        </Field>
      </Section>

      <Section title="Classification & location">
        <Field id="assetCategoryId" label="Category" error={errors.assetCategoryId}>
          <SelectMenu
            id="assetCategoryId"
            block
            placeholder="Choose a category…"
            value={values.assetCategoryId}
            onChange={(next) => setField('assetCategoryId', next)}
            options={categories.map((category) => ({ value: category.id, label: category.name }))}
            invalid={Boolean(errors.assetCategoryId)}
            describedBy={describedBy('assetCategoryId', errors)}
          />
        </Field>

        <Field id="roomId" label="Room" error={errors.roomId}>
          <SelectMenu
            id="roomId"
            block
            placeholder="Choose a room…"
            value={values.roomId}
            onChange={(next) => setField('roomId', next)}
            options={rooms.map((room) => ({ value: room.id, label: roomLabel(room) }))}
            invalid={Boolean(errors.roomId)}
            describedBy={describedBy('roomId', errors)}
          />
        </Field>
      </Section>

      <Section title="Dates & warranty">
        <Field id="installedOn" label="Installed on" error={errors.installedOn}>
          <input
            id="installedOn"
            name="installedOn"
            type="date"
            className={clsx(styles.input, styles.inputMono)}
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
            className={clsx(styles.input, styles.inputMono)}
            value={values.warrantyExpiresOn}
            onChange={handleChange}
            aria-invalid={errors.warrantyExpiresOn ? 'true' : 'false'}
            aria-describedby={describedBy('warrantyExpiresOn', errors, true)}
          />
        </Field>

        {canUseCategoryWarranty ? (
          <div className={styles.fieldWide}>
            <button type="button" className={styles.suggest} onClick={applyCategoryWarranty}>
              <Sparkles aria-hidden="true" />
              Use the {selectedCategory.name} default — {selectedCategory.defaultWarrantyMonths} months from installation
            </button>
          </div>
        ) : null}
      </Section>

      {isCreate ? null : (
        <fieldset className={styles.formSection}>
          <legend className={styles.formLegend}>Status</legend>
          <Segmented
            label="Status"
            value={values.status}
            onChange={(next) => setField('status', next)}
            options={ASSET_STATUSES.map((status) => ({ value: status, label: enumLabel(status) }))}
          />
          {errors.status ? (
            <p className={styles.fieldError}>{errors.status}</p>
          ) : (
            <p className={styles.fieldHint}>Retired is how equipment leaves the estate. The record and its history stay.</p>
          )}
        </fieldset>
      )}

      <div className={styles.formFooter}>
        <span className={styles.formFooterNote}>{isDirty ? 'Unsaved changes' : isCreate ? 'Starts out Active' : 'No changes yet'}</span>
        <MxButton onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </MxButton>
        <MxButton type="submit" variant="primary" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : isCreate ? 'Register asset' : 'Save changes'}
        </MxButton>
      </div>
    </form>
  );
}

export default AssetForm;
