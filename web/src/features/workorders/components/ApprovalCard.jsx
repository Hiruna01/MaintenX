import { Link } from 'react-router-dom';

import FailureSummaryPanel from '../../assets/components/FailureSummaryPanel';
import ServiceTimeline from '../../assets/components/ServiceTimeline';
import WarrantyBadge from '../../assets/components/WarrantyBadge';
import { formatDateTime } from '../services/workOrdersApi';
import ApprovalBasisStatement from './ApprovalBasisStatement';
import DecisionControls from './DecisionControls';
import DiagnosisPanel from './DiagnosisPanel';
import ProposalComparison from './ProposalComparison';

/**
 * One order waiting on a manager, with everything needed to decide it on this card: what
 * was reported, what it costs against the threshold, what the agent proposed and why, what
 * it diagnosed, and the machine's own history to check both against. Nothing here needs
 * another page opened — the links are there to go deeper, not to find the basics.
 *
 * Presentational apart from the decision controls: every figure is the API's. The service
 * history and failure summary are the asset registry's own components, so the history reads
 * here exactly as it does on the asset page — oldest first, every note verbatim.
 */
export function ApprovalCard({ item, onDecided }) {
  const { workOrder, asset, failureSummary, diagnosis, proposal } = item;
  const headingId = `approval-${workOrder.id}-heading`;

  return (
    <article className="approval-card" aria-labelledby={headingId}>
      <header className="approval-card__head">
        <div className="approval-card__tags">
          <span className="asset-tag asset-tag--large">Work order #{workOrder.id}</span>
          <Link to={`/assets/${asset.id}`} className="asset-tag">
            {asset.assetTag}
          </Link>
          <WarrantyBadge
            isUnderWarranty={failureSummary.isUnderWarranty}
            warrantyExpiresOn={asset.warrantyExpiresOn}
          />
          {failureSummary.isRepeatFailure ? <span className="repeat-flag">Repeat failure</span> : null}
        </div>

        <h2 id={headingId}>
          {asset.name}
          <span className="approval-card__room">
            {asset.room.code} · {asset.room.name}
          </span>
        </h2>

        <p className="approval-card__meta">
          Raised {formatDateTime(workOrder.createdAt)} ·{' '}
          <Link to={`/reports/${workOrder.reportId}`}>Report #{workOrder.reportId}</Link> ·{' '}
          <Link to={`/workorders/${workOrder.id}`}>Full order</Link>
        </p>

        {/* The reporter's own words, verbatim. */}
        <blockquote className="report-hero__description">{workOrder.reportDescription}</blockquote>
      </header>

      <ApprovalBasisStatement estimatedCost={workOrder.estimatedCost} basis={workOrder.approvalBasis} />

      {workOrder.revisionNote ? (
        <p className="approval-card__revision">
          <strong>Sent back before:</strong> {workOrder.revisionNote}
        </p>
      ) : null}

      <div className="approval-card__grid">
        <div className="approval-card__column">
          <ProposalComparison proposal={proposal} order={workOrder} reportId={workOrder.reportId} />
          <DiagnosisPanel diagnosis={diagnosis} reportId={workOrder.reportId} />
        </div>

        <div className="approval-card__column">
          <FailureSummaryPanel summary={failureSummary} headingId={`approval-${workOrder.id}-summary`} />

          <section className="approval-panel" aria-label="Service history">
            <header className="approval-panel__head">
              <h3>Service history</h3>
              <span className="approval-panel__source">Oldest first · notes verbatim</span>
            </header>

            {asset.serviceHistory.length === 0 ? (
              <p className="approval-panel__empty">
                No service visits on record — this machine has never been worked on.
              </p>
            ) : (
              <ServiceTimeline records={asset.serviceHistory} />
            )}
          </section>
        </div>
      </div>

      <DecisionControls
        orderId={workOrder.id}
        estimatedCost={workOrder.estimatedCost}
        onDecided={onDecided}
      />
    </article>
  );
}

export default ApprovalCard;
