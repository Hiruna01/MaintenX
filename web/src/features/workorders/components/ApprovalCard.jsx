import { History, MapPin, Repeat, RotateCcw } from 'lucide-react';
import { Link } from 'react-router-dom';

import { timeAgo } from '../../../components/ui/format';
import { Panel } from '../../../components/ui/Panel';
import { Pill } from '../../../components/ui/Pill';
import { EmptyState } from '../../../components/ui/States';
import HistoryFeed from '../../assets/components/HistoryFeed';
import SummaryCard from '../../assets/components/SummaryCard';
import TagChip from '../../assets/components/TagChip';
import WarrantyPill from '../../assets/components/WarrantyPill';
import { formatMoney } from '../services/workOrdersApi';
import styles from '../workorders.module.css';
import ApprovalBasisStatement from './ApprovalBasisStatement';
import DecisionControls from './DecisionControls';
import DiagnosisPanel from './DiagnosisPanel';
import ProposalComparison from './ProposalComparison';
import StrategyPill from './StrategyPill';

/**
 * One order waiting on a manager, with everything needed to decide it on this card: what
 * was reported, what it costs against the threshold, what the agent proposed and why, what
 * it diagnosed, and the machine's own history to check both against.
 *
 * Presentational apart from the decision controls: every figure is the API's. The history
 * and failure summary are the asset registry's own components, so they read here exactly as
 * on the asset page — oldest first, every note verbatim.
 */
export function ApprovalCard({ item, onDecided, index }) {
  const { workOrder, asset, failureSummary, diagnosis, proposal } = item;
  const headingId = `approval-${workOrder.id}-heading`;

  return (
    <article className={styles.caseCard} aria-labelledby={headingId} style={{ animationDelay: `${Math.min(index, 4) * 70}ms` }}>
      <header className={styles.caseHead}>
        <div className={styles.caseIdentity}>
          <div className={styles.caseTags}>
            <Link to={`/assets/${asset.id}`} className={styles.chipLink}>
              <TagChip tag={asset.assetTag} />
            </Link>
            <span className={styles.caseId}>WO #{workOrder.id}</span>
            <StrategyPill strategy={workOrder.strategy} />
            <WarrantyPill isUnderWarranty={failureSummary.isUnderWarranty} warrantyExpiresOn={asset.warrantyExpiresOn} />
            {failureSummary.isRepeatFailure ? (
              <Pill tone="red" icon={Repeat}>
                Repeat failure
              </Pill>
            ) : null}
          </div>

          <h2 id={headingId} className={styles.caseTitle}>
            {asset.name}
          </h2>

          <p className={styles.caseMeta}>
            <span>
              <MapPin aria-hidden="true" size={13} /> {asset.room.code} · {asset.room.name}
            </span>
            <span>Waiting {timeAgo(workOrder.createdAt).replace(' ago', '')}</span>
            <Link to={`/reports/${workOrder.reportId}`}>Report #{workOrder.reportId}</Link>
            <Link to={`/workorders/${workOrder.id}`}>Full order</Link>
          </p>
        </div>

        <div className={styles.caseCost}>
          <span className={styles.caseCostValue}>{formatMoney(workOrder.estimatedCost)}</span>
          <span className={styles.caseCostLabel}>estimate</span>
        </div>
      </header>

      {/* The reporter's own words, verbatim. */}
      <blockquote className={styles.fault}>{workOrder.reportDescription}</blockquote>

      <ApprovalBasisStatement estimatedCost={workOrder.estimatedCost} basis={workOrder.approvalBasis} />

      {workOrder.revisionNote ? (
        <p className={styles.revision}>
          <RotateCcw aria-hidden="true" />
          <span>
            <strong>Sent back before:</strong> {workOrder.revisionNote}
          </span>
        </p>
      ) : null}

      <div className={styles.caseGrid}>
        <div className={styles.column}>
          <ProposalComparison proposal={proposal} order={workOrder} reportId={workOrder.reportId} />
          <DiagnosisPanel diagnosis={diagnosis} reportId={workOrder.reportId} />
        </div>

        <div className={styles.column}>
          <SummaryCard summary={failureSummary} />
          <Panel eyebrow="Service history" count={asset.serviceHistory.length || null} actions={<span className={styles.source}>Oldest first · verbatim</span>}>
            {asset.serviceHistory.length === 0 ? (
              <EmptyState compact icon={History} title="No service visits on record" body="This machine has never been worked on." />
            ) : (
              <HistoryFeed records={asset.serviceHistory} />
            )}
          </Panel>
        </div>
      </div>

      <DecisionControls orderId={workOrder.id} estimatedCost={workOrder.estimatedCost} onDecided={onDecided} />
    </article>
  );
}

export default ApprovalCard;
