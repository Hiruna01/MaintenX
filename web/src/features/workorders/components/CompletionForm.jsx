import { useState } from 'react';

import Button from '../../../components/Button';
import { SERVICE_OUTCOMES } from '../../assets/services/assetsApi';
import { completeWorkOrder, enumLabel } from '../services/workOrdersApi';
import {
  EMPTY_COMPLETION_VALUES,
  PHOTO_URL_MAX,
  RESOLUTION_NOTE_MAX,
  validateCompletion,
} from '../services/workOrderValidation';

/**
 * The assigned technician closing the job: what it cost, how it ended, and what was done.
 *
 * Rendered only for the technician the order is assigned to, and the API checks that again
 * against the token. Completing appends a ServiceRecord to the asset's history in the same
 * transaction, and the note goes into it VERBATIM — it is what the diagnostic agent reads
 * next time this machine fails, so the form asks for it plainly and never tidies it.
 *
 * The outcome is a required choice with no default: a default would record a temporary fix
 * as resolved and erase the repeat-failure pattern the history exists to show.
 */
export function CompletionForm({ order, onCompleted }) {
  const [values, setValues] = useState(EMPTY_COMPLETION_VALUES);
  const [errors, setErrors] = useState({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(null);

  function handleChange(name, value) {
    setValues((current) => ({ ...current, [name]: value }));
  }

  async function handleSubmit(event) {
    event.preventDefault();

    const validation = validateCompletion(values);
    setErrors(validation);
    if (Object.keys(validation).length > 0) return;

    setIsSubmitting(true);
    setSubmitError(null);

    try {
      await completeWorkOrder(order.id, values);
      onCompleted('Job completed. It is now in the asset’s service history, and the reporter will be asked whether the fix held.');
    } catch (err) {
      setSubmitError(err.message);
      setIsSubmitting(false);
    }
  }

  return (
    <form className="asset-form completion-form" onSubmit={handleSubmit} noValidate>
      <div className="asset-form__grid">
        <label className="asset-form__field">
          <span>Actual cost (Rs)</span>
          <input
            inputMode="decimal"
            value={values.actualCost}
            onChange={(event) => handleChange('actualCost', event.target.value)}
            aria-invalid={errors.actualCost ? 'true' : undefined}
            placeholder="e.g. 8500"
          />
          {errors.actualCost ? <span className="form__error">{errors.actualCost}</span> : null}
        </label>

        <label className="asset-form__field">
          <span>Outcome</span>
          <select
            value={values.outcome}
            onChange={(event) => handleChange('outcome', event.target.value)}
            aria-invalid={errors.outcome ? 'true' : undefined}
          >
            <option value="">Choose how it ended…</option>
            {SERVICE_OUTCOMES.map((outcome) => (
              <option key={outcome} value={outcome}>
                {enumLabel(outcome)}
              </option>
            ))}
          </select>
          {errors.outcome ? <span className="form__error">{errors.outcome}</span> : null}
        </label>
      </div>

      <label className="asset-form__field">
        <span>What was done</span>
        <textarea
          rows={4}
          value={values.resolutionNote}
          maxLength={RESOLUTION_NOTE_MAX}
          onChange={(event) => handleChange('resolutionNote', event.target.value)}
          aria-invalid={errors.resolutionNote ? 'true' : undefined}
        />
        {errors.resolutionNote ? (
          <span className="form__error">{errors.resolutionNote}</span>
        ) : (
          <span className="asset-form__hint">
            Saved word for word in the asset&apos;s service history. {values.resolutionNote.length}/
            {RESOLUTION_NOTE_MAX}
          </span>
        )}
      </label>

      <label className="asset-form__field">
        <span>
          Photo link <span className="asset-form__optional">optional</span>
        </span>
        <input
          type="url"
          value={values.completionPhotoUrl}
          maxLength={PHOTO_URL_MAX}
          onChange={(event) => handleChange('completionPhotoUrl', event.target.value)}
          aria-invalid={errors.completionPhotoUrl ? 'true' : undefined}
          placeholder="https://…"
        />
        {errors.completionPhotoUrl ? (
          <span className="form__error">{errors.completionPhotoUrl}</span>
        ) : null}
      </label>

      {submitError ? (
        <p className="form__error" role="alert">
          {submitError}
        </p>
      ) : null}

      <div className="asset-form__actions">
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Completing…' : 'Complete work order'}
        </Button>
      </div>
    </form>
  );
}

export default CompletionForm;
