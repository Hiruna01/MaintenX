import clsx from 'clsx';
import { Link } from 'react-router-dom';

import { formatInstant } from '../../../components/ui/format';
import { Panel } from '../../../components/ui/Panel';
import { Pill } from '../../../components/ui/Pill';
import { adviceLabel, formatMoney } from '../services/workOrdersApi';
import styles from '../workorders.module.css';
import StrategyPill from './StrategyPill';

const URGENCY_TONES = { high: 'red', medium: 'amber', low: 'slate' };

/**
 * What the strategist PROPOSED beside the work order AS RAISED. The agent proposes, a manager
 * raises the order, and the manager may plan or cost it differently; side by side, the reader
 * sees that without being told. A row where the two differ is marked — the mark decides
 * nothing, and the order is what is being approved either way.
 */
export function ProposalComparison({ proposal, order, reportId }) {
  const ok = readable(proposal);

  return (
    <Panel eyebrow="Proposal" actions={<span className={styles.source}>Strategist · advice</span>}>
      <ProposalState proposal={proposal} reportId={reportId} />

      <div className={styles.compare} role="table" aria-label="Agent proposal compared with the order as raised">
        <div className={clsx(styles.compareRow, styles.compareHead)} role="row">
          <span role="columnheader">
            <span className="mx-visually-hidden">Field</span>
          </span>
          <span role="columnheader">Agent proposed</span>
          <span role="columnheader">Order as raised</span>
        </div>
        <Row
          label="Strategy"
          proposed={ok ? <StrategyPill strategy={proposal.strategy} /> : '—'}
          raised={<StrategyPill strategy={order.strategy} />}
          differs={ok && proposal.strategy !== order.strategy}
        />
        <Row
          label="Estimate"
          proposed={ok ? <span className={styles.money}>{formatMoney(proposal.estimatedCost)}</span> : '—'}
          raised={<span className={styles.money}>{formatMoney(order.estimatedCost)}</span>}
          // Equality of two figures the API sent, to mark a row — never a threshold check.
          differs={ok && Number(proposal.estimatedCost) !== Number(order.estimatedCost)}
        />
        <Row
          label="Urgency"
          proposed={
            ok ? (
              <Pill tone={URGENCY_TONES[proposal.urgency] ?? 'slate'}>{adviceLabel(proposal.urgency)}</Pill>
            ) : (
              '—'
            )
          }
          raised={<span className={styles.none}>Not recorded on an order</span>}
        />
        <Row
          label="Parts"
          proposed={<span className={styles.none}>Not part of a proposal</span>}
          raised={order.partsRequired ?? <span className={styles.none}>None listed</span>}
        />
      </div>

      {ok ? (
        <>
          <p className={styles.miniLabel}>Justification</p>
          {/* Verbatim: the agent's account of why, for the person deciding. */}
          <blockquote className={styles.quote}>{proposal.justification}</blockquote>

          {proposal.consolidateWithWorkOrderIds.length > 0 ? (
            <p className={styles.note}>
              Proposes combining with{' '}
              {proposal.consolidateWithWorkOrderIds.map((id, index) => (
                <span key={id}>
                  {index > 0 ? ', ' : ''}
                  <Link to={`/workorders/${id}`}>work order #{id}</Link>
                </span>
              ))}
              . An id here is the agent&apos;s claim until someone opens the order.
            </p>
          ) : null}

          <p className={styles.footnote}>
            From workflow #{proposal.workflowId}, recorded {formatInstant(proposal.recordedAt)}.
          </p>
        </>
      ) : null}
    </Panel>
  );
}

function readable(proposal) {
  return Boolean(proposal && proposal.outputReadable);
}

function Row({ label, proposed, raised, differs = false }) {
  return (
    <div className={clsx(styles.compareRow, differs && styles.compareDiffers)} role="row">
      <span role="rowheader" className={styles.compareLabel}>
        {label}
        {differs ? <span className={styles.differs}>differs</span> : null}
      </span>
      <span role="cell">{proposed}</span>
      <span role="cell">{raised}</span>
    </div>
  );
}

/**
 * Null and failed are different facts, and neither is a proposal to defer: no step means the
 * strategist never ran on this report; a failed step means it ran and could not answer.
 */
function ProposalState({ proposal, reportId }) {
  if (!proposal) {
    return (
      <p className={styles.empty}>
        No proposal recorded — the strategist has not run on this report. Decide from the order and the history.
      </p>
    );
  }

  if (proposal.validationResult !== 'Ok') {
    return (
      <p className={clsx(styles.empty, styles.emptyFailed)}>
        The strategist ran ({formatInstant(proposal.recordedAt)}) but could not produce a proposal
        {proposal.errorMessage ? `: ${proposal.errorMessage}` : '.'}
      </p>
    );
  }

  if (!proposal.outputReadable) {
    return (
      <p className={clsx(styles.empty, styles.emptyFailed)}>
        A proposal was recorded but could not be read. The raw output is in the{' '}
        <Link to={`/reports/${reportId}`}>report&apos;s agent reasoning</Link>.
      </p>
    );
  }

  return null;
}

export default ProposalComparison;
