import { Link } from 'react-router-dom';

import { adviceLabel, formatDateTime, formatMoney } from '../services/workOrdersApi';
import StrategyBadge from './StrategyBadge';

/**
 * What the strategist PROPOSED beside the work order AS RAISED. They are not the same thing:
 * the agent proposes, a manager raises the order, and the manager may plan or cost it
 * differently. Putting them side by side lets the reader see that without being told.
 *
 * A row where the two differ is marked "differs". That marker decides nothing — it points a
 * reader at a difference, and the order is what is being approved either way.
 */
export function ProposalComparison({ proposal, order, reportId }) {
  return (
    <section className="approval-panel" aria-label="The agent's proposal">
      <header className="approval-panel__head">
        <h3>Proposal</h3>
        <span className="approval-panel__source">ResolutionStrategist · advice</span>
      </header>

      <ProposalState proposal={proposal} reportId={reportId} />

      <table className="proposal-table">
        <thead>
          <tr>
            <th scope="col">
              <span className="visually-hidden">Field</span>
            </th>
            <th scope="col">Agent proposed</th>
            <th scope="col">Order as raised</th>
          </tr>
        </thead>
        <tbody>
          <Row
            label="Strategy"
            proposed={readable(proposal) ? <StrategyBadge strategy={proposal.strategy} /> : '—'}
            raised={<StrategyBadge strategy={order.strategy} />}
            differs={readable(proposal) && proposal.strategy !== order.strategy}
          />
          <Row
            label="Estimated cost"
            proposed={readable(proposal) ? formatMoney(proposal.estimatedCost) : '—'}
            raised={formatMoney(order.estimatedCost)}
            // Equality of two figures the API sent, to mark a row — never a threshold check.
            differs={readable(proposal) && Number(proposal.estimatedCost) !== Number(order.estimatedCost)}
          />
          <Row
            label="Urgency"
            proposed={
              readable(proposal) ? (
                <span className={`urgency urgency--${proposal.urgency}`}>{adviceLabel(proposal.urgency)}</span>
              ) : (
                '—'
              )
            }
            raised={<span className="proposal-table__none">Not recorded on an order</span>}
          />
          <Row
            label="Parts"
            proposed={<span className="proposal-table__none">Not part of a proposal</span>}
            raised={order.partsRequired ?? <span className="proposal-table__none">None listed</span>}
          />
        </tbody>
      </table>

      {readable(proposal) ? (
        <>
          <p className="approval-panel__label">Justification</p>
          {/* Verbatim: the agent's account of why, for the person deciding. */}
          <blockquote className="approval-panel__quote">{proposal.justification}</blockquote>

          {proposal.consolidateWithWorkOrderIds.length > 0 ? (
            <p className="approval-panel__note">
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

          <p className="approval-panel__footnote">
            From workflow #{proposal.workflowId}, recorded {formatDateTime(proposal.recordedAt)}.
          </p>
        </>
      ) : null}
    </section>
  );
}

function readable(proposal) {
  return Boolean(proposal && proposal.outputReadable);
}

function Row({ label, proposed, raised, differs = false }) {
  return (
    <tr className={differs ? 'proposal-table__row--differs' : undefined}>
      <th scope="row">
        {label}
        {differs ? <span className="proposal-table__differs">differs</span> : null}
      </th>
      <td>{proposed}</td>
      <td>{raised}</td>
    </tr>
  );
}

/**
 * Null and failed are different facts, and neither is a proposal to defer: no step means the
 * strategist never ran on this report; a failed step means it ran and could not answer.
 */
function ProposalState({ proposal, reportId }) {
  if (!proposal) {
    return (
      <p className="approval-panel__empty">
        No proposal recorded — the strategist has not run on this report. Decide from the order
        and the history below.
      </p>
    );
  }

  if (proposal.validationResult !== 'Ok') {
    return (
      <p className="approval-panel__empty approval-panel__empty--failed">
        The strategist ran ({formatDateTime(proposal.recordedAt)}) but could not produce a
        proposal{proposal.errorMessage ? `: ${proposal.errorMessage}` : '.'}
      </p>
    );
  }

  if (!proposal.outputReadable) {
    return (
      <p className="approval-panel__empty approval-panel__empty--failed">
        A proposal was recorded but could not be read. The raw output is in the{' '}
        <Link to={`/reports/${reportId}`}>report&apos;s agent reasoning</Link>.
      </p>
    );
  }

  return null;
}

export default ProposalComparison;
