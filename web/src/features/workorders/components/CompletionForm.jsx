import { CircleCheckBig } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import Segmented from '../../../components/ui/Segmented';
import { SERVICE_OUTCOMES } from '../../assets/services/assetsApi';
import { completeWorkOrder, enumLabel } from '../services/workOrdersApi';
import {
  EMPTY_COMPLETION_VALUES,
  PHOTO_URL_MAX,
  RESOLUTION_NOTE_MAX,
  validateCompletion,
} from '../services/workOrderValidation';
import styles from '../workorders.module.css';

/**
 * The assigned technician closing the job: what it cost, how it ended, and what was done.
 *
 * Rendered only for the technician the order is assigned to, and the API checks that again
 * against the token. Completing appends a ServiceRecord to the asset's history in the same
 * transaction, and the note goes into it VERBATIM — it is what the diagnostic agent reads
 * next time this machine fails.
 *
 * The outcome is a required choice with NO DEFAULT — no segment starts selected: a default
 * would record a temporary fix as resolved and erase the repeat-failure pattern.
 */
export function CompletionForm({ order, onCompleted }) {
  const [values, setValues] = useState(EMPTY_COMPLETION_VALUES);
  const [errors, setErrors] = useState({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(null);

  function handleChange(name, value) {
    setValues((current) => ({ ...current, [name]: value }));
    setErrors((current) => ({ ...current, [name]: undefined }));
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
    <form className={styles.completionForm} onSubmit={handleSubmit} noValidate>
      <div className={form.field}>
        <span className={form.label} id="completion-outcome-label">
          How did it end?
        </span>
        <Segmented
          label="Outcome"
          value={values.outcome}
          onChange={(value) => handleChange('outcome', value)}
          options={SERVICE_OUTCOMES.map((outcome) => ({ value: outcome, label: enumLabel(outcome) }))}
        />
        {errors.outcome ? <p className={form.error}>{errors.outcome}</p> : null}
      </div>

      <div className={form.grid}>
        <div className={form.field}>
          <label htmlFor="completion-cost" className={form.label}>
            Actual cost (Rs)
          </label>
          <input
            id="completion-cost"
            inputMode="decimal"
            className={`${form.input} ${form.mono}`}
            value={values.actualCost}
            onChange={(event) => handleChange('actualCost', event.target.value)}
            aria-invalid={errors.actualCost ? 'true' : undefined}
            placeholder="e.g. 8500"
          />
          {errors.actualCost ? <p className={form.error}>{errors.actualCost}</p> : null}
        </div>

        <div className={form.field}>
          <label htmlFor="completion-photo" className={form.label}>
            Photo link <span className={form.optional}>Optional</span>
          </label>
          <input
            id="completion-photo"
            type="url"
            className={form.input}
            value={values.completionPhotoUrl}
            maxLength={PHOTO_URL_MAX}
            onChange={(event) => handleChange('completionPhotoUrl', event.target.value)}
            aria-invalid={errors.completionPhotoUrl ? 'true' : undefined}
            placeholder="https://…"
          />
          {errors.completionPhotoUrl ? <p className={form.error}>{errors.completionPhotoUrl}</p> : null}
        </div>
      </div>

      <div className={form.field}>
        <label htmlFor="completion-note" className={form.label}>
          What was done
        </label>
        <textarea
          id="completion-note"
          rows={5}
          className={form.textarea}
          value={values.resolutionNote}
          maxLength={RESOLUTION_NOTE_MAX}
          onChange={(event) => handleChange('resolutionNote', event.target.value)}
          aria-invalid={errors.resolutionNote ? 'true' : undefined}
          placeholder="What you found, what you did, and whether it is a lasting fix."
        />
        {errors.resolutionNote ? (
          <p className={form.error}>{errors.resolutionNote}</p>
        ) : (
          <p className={form.hint}>
            <span>Saved word for word in the asset&apos;s service history.</span>
            <span className={form.counter}>
              {values.resolutionNote.length}/{RESOLUTION_NOTE_MAX}
            </span>
          </p>
        )}
      </div>

      {submitError ? (
        <p className={form.submitError} role="alert">
          {submitError}
        </p>
      ) : null}

      <div className={form.actions}>
        <MxButton type="submit" variant="primary" icon={CircleCheckBig} disabled={isSubmitting}>
          {isSubmitting ? 'Completing…' : 'Complete work order'}
        </MxButton>
      </div>
    </form>
  );
}

export default CompletionForm;
