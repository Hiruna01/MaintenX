import { Link } from 'react-router-dom';

import { formatInstant } from '../../../components/ui/format';
import { Pill } from '../../../components/ui/Pill';
import { attemptsLabel, validationOutcome } from '../../reports/services/agentSteps';
import styles from '../agents.module.css';
import { agentLabel, describeCost, formatDurationMs, formatTokens } from '../services/agentMetricsApi';

const OUTCOME_TONES = { ok: 'green', neutral: 'slate', failed: 'red', waiting: 'amber' };

/**
 * Agent runs as the API ranked them — the slowest, or the ones that reported the most tokens.
 * Each links to its workflow, whose audit trail is the full trace: the plan, every tool call,
 * the output verbatim. Nothing here is re-sorted.
 */
export function RunList({ runs, pricing }) {
  return (
    <ol className={styles.runs}>
      {runs.map((run) => {
        const outcome = validationOutcome(run.validationResult);
        const reported = run.promptTokens !== null && run.completionTokens !== null;

        return (
          <li key={run.stepId} className={styles.run}>
            <div className={styles.runHead}>
              <Link to={`/workflows/${run.workflowId}`} className={styles.runName}>
                {agentLabel(run.agentName)}
                <span className={styles.runWorkflow}>workflow #{run.workflowId}</span>
              </Link>
              <Pill tone={OUTCOME_TONES[outcome.tone] ?? 'slate'}>{outcome.label}</Pill>
            </div>

            <span className={styles.sub}>
              {formatInstant(run.createdAt)}
              {attemptsLabel(run) ? ` · ${attemptsLabel(run)}` : ''}
            </span>

            <dl className={styles.runFigures}>
              <div>
                <dt>Time</dt>
                <dd>{run.durationMs === 0 ? 'timed with the run' : formatDurationMs(run.durationMs)}</dd>
              </div>
              <div>
                <dt>Tokens in / out</dt>
                <dd>{reported ? `${formatTokens(run.promptTokens)} / ${formatTokens(run.completionTokens)}` : 'not reported'}</dd>
              </div>
              {pricing?.configured ? (
                <div>
                  <dt>Est. cost</dt>
                  <dd>{describeCost(run.estimatedCostUsd, pricing, reported ? 1 : 0)}</dd>
                </div>
              ) : null}
            </dl>
          </li>
        );
      })}
    </ol>
  );
}

export default RunList;
