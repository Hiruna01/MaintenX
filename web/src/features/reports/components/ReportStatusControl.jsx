import { ArrowRight } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { Panel } from '../../../components/ui/Panel';
import { StatusPill } from '../../../components/ui/Pill';
import { SelectMenu } from '../../../components/ui/SelectMenu';
import { humanize } from '../../../components/ui/tones';
import { REPORT_STATUSES, updateReportStatus, validateStatusChange } from '../services/reportsApi';
import styles from '../reports.module.css';

/**
 * PATCH /api/reports/{id}/status for a FacilitiesManager — to close a duplicate, or a fault
 * that turned out to be nothing, without walking it through diagnosis.
 *
 * THE API DECIDES. Every status other than the current one is offered, and the report
 * lifecycle in ReportService says whether the move is legal: a 409 is shown exactly as the API
 * worded it. This client keeps no copy of the map — not even "Closed is terminal".
 *
 * Two steps, like the approval decisions: choose, then confirm. Success remounts the page.
 */
export function ReportStatusControl({ report, onChanged }) {
  const [status, setStatus] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [errors, setErrors] = useState({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(null);

  const options = REPORT_STATUSES.filter((name) => name !== report.status).map((name) => ({
    value: name,
    label: humanize(name),
  }));

  function choose(next) {
    setStatus(next);
    setErrors({});
    setSubmitError(null);
  }

  function handleReview(event) {
    event.preventDefault();
    const validation = validateStatusChange(status);
    setErrors(validation);
    if (Object.keys(validation).length === 0) setConfirming(true);
  }

  async function handleConfirm() {
    setIsSubmitting(true);
    setSubmitError(null);

    try {
      await updateReportStatus(report.id, status);
      onChanged();
    } catch (error) {
      // As sent — a 409 is the lifecycle refusing the move, in the API's own words.
      setSubmitError(error.message);
      setIsSubmitting(false);
      setConfirming(false);
    }
  }

  const fieldId = `report-status-${report.id}`;

  return (
    <Panel eyebrow="Report status">
      <p className={styles.sectionLead}>
        Move this report yourself — for example, close it as a duplicate. The report lifecycle decides which moves are
        allowed, and a refused one is shown as the API sent it.
      </p>

      {confirming ? (
        <div className={styles.statusConfirm}>
          <p className={styles.statusMove}>
            <StatusPill status={report.status} />
            <ArrowRight aria-hidden="true" size={16} />
            <StatusPill status={status} />
          </p>
          <div className={form.actions}>
            <MxButton onClick={() => setConfirming(false)} disabled={isSubmitting}>
              Cancel
            </MxButton>
            <MxButton variant="primary" onClick={handleConfirm} disabled={isSubmitting}>
              {isSubmitting ? 'Sending…' : 'Confirm move'}
            </MxButton>
          </div>
        </div>
      ) : (
        <form onSubmit={handleReview} noValidate>
          <div className={form.field}>
            <label htmlFor={fieldId} className={form.label}>
              Move to
            </label>
            <SelectMenu
              id={fieldId}
              block
              value={status}
              onChange={choose}
              options={options}
              placeholder="Choose a status"
              invalid={Boolean(errors.status)}
              describedBy={errors.status ? `${fieldId}-error` : undefined}
            />
            {errors.status ? (
              <p id={`${fieldId}-error`} className={form.error}>
                {errors.status}
              </p>
            ) : null}
          </div>
          <div className={form.actions}>
            <MxButton type="submit">Review move</MxButton>
          </div>
        </form>
      )}

      {submitError ? (
        <p className={form.submitError} role="alert">
          {submitError}
        </p>
      ) : null}
    </Panel>
  );
}

export default ReportStatusControl;
