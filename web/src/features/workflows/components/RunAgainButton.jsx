import { RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { startWorkflow } from '../services/workflowsService';

/**
 * "Run the agents again" for a report whose run FAILED — the agent service was down, timed
 * out, or could not produce an answer. It starts a new workflow on the same report and opens
 * it; the failed one stays as it is, with its reason, beside the new one.
 *
 * Offered only for a failed run that belongs to a report. Whether a new run may start is the
 * API's rule, not this button's: it refuses with 409 while the report's latest run is still
 * live (one report, one live run) or the report is closed, and that message is shown here as
 * sent.
 */
export function RunAgainButton({ workflow }) {
  const navigate = useNavigate();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState(null);

  if (workflow.currentState !== 'Failed' || !workflow.reportId) return null;

  async function handleClick() {
    setIsSubmitting(true);
    setError(null);

    try {
      const started = await startWorkflow(workflow.objective, workflow.reportId);
      navigate(`/workflows/${started.id}`);
    } catch (caught) {
      setError(caught.message);
      setIsSubmitting(false);
    }
  }

  return (
    <>
      <MxButton icon={RefreshCw} onClick={handleClick} disabled={isSubmitting}>
        {isSubmitting ? 'Starting…' : 'Run the agents again'}
      </MxButton>
      {error ? (
        <p className={form.submitError} role="alert">
          {error}
        </p>
      ) : null}
    </>
  );
}

export default RunAgainButton;
