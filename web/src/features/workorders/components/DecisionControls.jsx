import clsx from 'clsx';
import { Check, PencilLine, X } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { approveWorkOrder, formatMoney, rejectWorkOrder, requestRevision } from '../services/workOrdersApi';
import { DECISION_NOTE_MAX, validateDecisionNote } from '../services/workOrderValidation';
import styles from '../workorders.module.css';

const MODES = {
  approve: {
    confirmLabel: 'Confirm approval',
    done: (id) => `Work order #${id} approved. It can now be assigned and booked.`,
  },
  reject: {
    field: 'Reason for rejecting',
    hint: 'Required. Whoever raises the next order for this fault reads it.',
    confirmLabel: 'Reject work order',
    done: (id) => `Work order #${id} rejected, with your reason recorded.`,
  },
  revise: {
    field: 'What should change',
    hint: 'Required. The order goes back to Draft with this note on it.',
    confirmLabel: 'Send back for revision',
    done: (id) => `Work order #${id} sent back to Draft with your note.`,
  },
};

/**
 * Approve, Reject, Request revision — the three answers a manager can give, and nothing else.
 *
 * Each is two steps: choose, then confirm. Approving is spending money, so it is never one
 * stray click; rejecting and sending back each need a written reason, checked here by
 * validate() and again by the API, which refuses a blank one with a 400.
 *
 * What a decision MEANS — the status it moves the order to, the workflow it moves with it,
 * who is recorded as deciding — is C# in WorkOrderService. This sends the choice and shows
 * the answer, including a 409 when somebody else has decided the order first.
 */
export function DecisionControls({ orderId, estimatedCost, onDecided }) {
  const [mode, setMode] = useState(null);
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(null);

  function choose(nextMode) {
    setMode(nextMode);
    setNote('');
    setErrors({});
    setSubmitError(null);
  }

  async function handleConfirm(event) {
    event.preventDefault();

    const validation = mode === 'approve' ? {} : validateDecisionNote(note, MODES[mode].field);
    setErrors(validation);
    if (Object.keys(validation).length > 0) return;

    setIsSubmitting(true);
    setSubmitError(null);

    try {
      if (mode === 'approve') await approveWorkOrder(orderId);
      if (mode === 'reject') await rejectWorkOrder(orderId, note);
      if (mode === 'revise') await requestRevision(orderId, note);
      onDecided(MODES[mode].done(orderId));
    } catch (error) {
      setSubmitError(
        error.status === 409 ? `${error.message} Somebody may have decided it already — refresh the queue.` : error.message,
      );
      setIsSubmitting(false);
    }
  }

  if (mode === null) {
    return (
      <div className={styles.decision}>
        <p className={styles.decisionPrompt}>
          Your decision
          <span>Approve spends the estimate. Reject and revision each need a written reason.</span>
        </p>
        <div className={styles.decisionButtons}>
          <MxButton onClick={() => choose('revise')} icon={PencilLine}>
            Request revision
          </MxButton>
          <MxButton variant="danger" onClick={() => choose('reject')} icon={X}>
            Reject
          </MxButton>
          <MxButton variant="primary" onClick={() => choose('approve')} icon={Check}>
            Approve
          </MxButton>
        </div>
      </div>
    );
  }

  const config = MODES[mode];
  const fieldId = `decision-note-${orderId}`;

  return (
    <form className={clsx(styles.decision, styles.decisionConfirm, styles[`decision-${mode}`])} onSubmit={handleConfirm} noValidate>
      {mode === 'approve' ? (
        <p className={styles.decisionQuestion}>
          Approve work order #{orderId} at an estimated <strong>{formatMoney(estimatedCost)}</strong>? You will be
          recorded as the manager who approved it.
        </p>
      ) : (
        <div className={form.field} style={{ flex: 1 }}>
          <label htmlFor={fieldId} className={form.label}>
            {config.field}
          </label>
          <textarea
            id={fieldId}
            rows={3}
            className={form.textarea}
            value={note}
            maxLength={DECISION_NOTE_MAX}
            onChange={(event) => setNote(event.target.value)}
            aria-invalid={errors.note ? 'true' : undefined}
            aria-describedby={`${fieldId}-hint`}
            autoFocus
          />
          {errors.note ? (
            <p id={`${fieldId}-hint`} className={form.error}>
              {errors.note}
            </p>
          ) : (
            <p id={`${fieldId}-hint`} className={form.hint}>
              <span>{config.hint}</span>
              <span className={form.counter}>
                {note.length}/{DECISION_NOTE_MAX}
              </span>
            </p>
          )}
        </div>
      )}

      {submitError ? (
        <p className={form.submitError} role="alert">
          {submitError}
        </p>
      ) : null}

      <div className={styles.decisionButtons}>
        <MxButton onClick={() => choose(null)} disabled={isSubmitting}>
          Cancel
        </MxButton>
        <MxButton type="submit" variant={mode === 'reject' ? 'danger' : 'primary'} disabled={isSubmitting}>
          {isSubmitting ? 'Sending…' : config.confirmLabel}
        </MxButton>
      </div>
    </form>
  );
}

export default DecisionControls;
