import { AlarmClock, CircleCheck, CircleHelp, CircleX } from 'lucide-react';

import { Pill } from '../../../components/ui/Pill';
import { AGENT_OUTCOMES, agentOutcomeLabel } from '../services/verificationApi';

/**
 * The reporter's answer as a pill. Null is "not answered", never "no" — a check nobody has
 * answered is not a failed repair.
 */
export function AnswerPill({ confirmed }) {
  if (confirmed === true) return <Pill tone="green" icon={CircleCheck}>Yes, fixed</Pill>;
  if (confirmed === false) return <Pill tone="red" icon={CircleX}>No, still broken</Pill>;
  return <Pill tone="slate" icon={CircleHelp}>Not answered</Pill>;
}

const OUTCOME_TONES = {
  [AGENT_OUTCOMES.confirm]: 'green',
  [AGENT_OUTCOMES.reopen]: 'amber',
  [AGENT_OUTCOMES.escalate]: 'red',
};

/**
 * The agent's verdict as a pill, keyed by the stored string. A value that is not one of
 * AGENT_OUTCOMES stays grey and shows its raw text rather than being mapped onto a verdict it
 * may not mean.
 */
export function AgentOutcomePill({ outcome, short = false }) {
  const tone = OUTCOME_TONES[outcome] ?? 'slate';
  const label = short && OUTCOME_TONES[outcome] ? outcome.charAt(0).toUpperCase() + outcome.slice(1) : agentOutcomeLabel(outcome);
  return <Pill tone={tone}>{label}</Pill>;
}

/**
 * The overdue marker. It shows the API's `isOverdue` and nothing else — which deadline was
 * missed is decided in VerificationService on the sweep's own clocks.
 */
export function OverduePill({ status }) {
  const waitingOn =
    status === 'Pending' ? 'Due, and the sweep has not asked the reporter yet.' : 'The reporter has not answered within the response window.';
  return (
    <span title={waitingOn}>
      <Pill tone="red" icon={AlarmClock}>
        Overdue
      </Pill>
    </span>
  );
}
