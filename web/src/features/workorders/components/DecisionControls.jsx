import { useState } from 'react';

import Button from '../../../components/Button';
import {
  approveWorkOrder,
  formatMoney,
  rejectWorkOrder,
  requestRevision,
} from '../services/workOrdersApi';
import { DECISION_NOTE_MAX, validateDecisionNote } from '../services/workOrderValidation';

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
        error.status === 409
          ? `${error.message} Somebody may have decided it already — refresh the queue.`
          : error.message,
      );
      setIsSubmitting(false);
    }
  }

  if (mode === null) {
    return (
      <div className="decision">
        <p className="decision__prompt">Your decision</p>
        <div className="decision__buttons">
          <Button onClick={() => choose('approve')}>Approve</Button>
          <Button variant="danger" onClick={() => choose('reject')}>
            Reject
          </Button>
          <Button variant="secondary" onClick={() => choose('revise')}>
            Request revision
          </Button>
        </div>
      </div>
    );
  }

  const config = MODES[mode];
  const fieldId = `decision-note-${orderId}`;

  return (
    <form className={`decision decision--${mode}`} onSubmit={handleConfirm} noValidate>
      {mode === 'approve' ? (
        <p className="decision__confirm">
          Approve work order #{orderId} at an estimated <strong>{formatMoney(estimatedCost)}</strong>?
          You will be recorded as the manager who approved it.
        </p>
      ) : (
        <div className="decision__field">
          <label htmlFor={fieldId}>{config.field}</label>
          <textarea
            id={fieldId}
            rows={3}
            value={note}
            maxLength={DECISION_NOTE_MAX}
            onChange={(event) => setNote(event.target.value)}
            aria-invalid={errors.note ? 'true' : undefined}
            aria-describedby={`${fieldId}-hint`}
            autoFocus
          />
          <p id={`${fieldId}-hint`} className={errors.note ? 'form__error' : 'decision__hint'}>
            {errors.note ?? `${config.hint} ${note.length}/${DECISION_NOTE_MAX}`}
          </p>
        </div>
      )}

      {submitError ? (
        <p className="form__error" role="alert">
          {submitError}
        </p>
      ) : null}

      <div className="decision__buttons">
        <Button type="submit" variant={mode === 'reject' ? 'danger' : 'primary'} disabled={isSubmitting}>
          {isSubmitting ? 'Sending…' : config.confirmLabel}
        </Button>
        <Button variant="secondary" onClick={() => choose(null)} disabled={isSubmitting}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

export default DecisionControls;
