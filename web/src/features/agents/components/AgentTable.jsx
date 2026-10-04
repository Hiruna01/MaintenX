import styles from '../agents.module.css';
import { agentLabel, describeCost, formatDurationMs, formatTokens, ofRuns } from '../services/agentMetricsApi';
import { formatPercent } from '../../verification/services/verificationApi';

/**
 * One row per agent, in the order the API sends them — all five, even an agent that never ran
 * in the range. Every figure is the API's: the rate sits beside the counts it was divided
 * from, latency is over the runs the API could time, and tokens over the runs that reported
 * them.
 */
export function AgentTable({ agents, pricing }) {
  return (
    <div className={styles.tableScroll}>
      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">Agent</th>
            <th scope="col">Runs</th>
            <th scope="col">Failed</th>
            <th scope="col">Retried</th>
            <th scope="col">Median</th>
            <th scope="col">p95</th>
            <th scope="col">Avg tokens</th>
            <th scope="col">Est. cost</th>
          </tr>
        </thead>
        <tbody>
          {agents.map((agent) => (
            <tr key={agent.agentName}>
              <th scope="row">{agentLabel(agent.agentName)}</th>
              <td className={styles.numCell}>{agent.runs}</td>
              <td>
                <span className={styles.num}>{agent.runs === 0 ? '—' : formatPercent(agent.failureRate)}</span>
                <span className={styles.sub}>{ofRuns(agent.failed, agent.runs)}</span>
              </td>
              <td>
                <span className={styles.num}>
                  {agent.runsReportingAttempts === 0 ? '—' : formatPercent(agent.retryRate)}
                </span>
                <span className={styles.sub}>{ofRuns(agent.retriedRuns, agent.runsReportingAttempts)}</span>
              </td>
              <td className={styles.numCell}>{formatDurationMs(agent.medianDurationMs)}</td>
              <td className={styles.numCell}>{formatDurationMs(agent.p95DurationMs)}</td>
              <td>
                <span className={styles.num}>{formatTokens(agent.averageTokensPerRun)}</span>
                <span className={styles.sub}>{ofRuns(agent.runsWithUsage, agent.runs, 'reported')}</span>
              </td>
              {/* With no price configured the panel's note says so once; each cell is a dash. */}
              <td className={styles.numCell}>
                {pricing?.configured ? describeCost(agent.estimatedCostUsd, pricing, agent.runsWithUsage) : '—'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default AgentTable;
