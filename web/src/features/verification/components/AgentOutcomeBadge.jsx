import { AGENT_OUTCOMES, agentOutcomeLabel } from '../services/verificationApi';

/**
 * The agent's verdict as a pill. The modifier is the stored string itself ("reopen"); a value
 * that is not one of AGENT_OUTCOMES gets no modifier and stays the neutral grey, showing its raw
 * text rather than being mapped onto a verdict it may not mean.
 */
export function AgentOutcomeBadge({ outcome }) {
  const known = Object.values(AGENT_OUTCOMES).includes(outcome);

  return (
    <span className={`agent-outcome ${known ? `agent-outcome--${outcome}` : ''}`.trim()}>
      {agentOutcomeLabel(outcome)}
    </span>
  );
}

export default AgentOutcomeBadge;
