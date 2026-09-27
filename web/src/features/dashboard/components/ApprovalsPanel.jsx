import { Stamp } from 'lucide-react';

import MxButton from '../../../components/ui/Button';
import { timeAgo } from '../../../components/ui/format';
import { Panel } from '../../../components/ui/Panel';
import { Pill } from '../../../components/ui/Pill';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useFetch from '../../../hooks/useFetch';
import TagChip from '../../assets/components/TagChip';
import { formatMoney, strategyLabel } from '../../workorders/services/workOrdersApi';
import { approvalQueuePath } from '../services/dashboardApi';
import styles from '../dashboard.module.css';
import { LinkRow, ListSkeleton } from './parts';

/**
 * The approval queue, oldest first, as the API sends it. Whether an order is above the
 * threshold or a replacement is the API's `approvalBasis` — nothing here compares money.
 */
export function ApprovalsPanel() {
  const { data, isLoading, error } = useFetch(approvalQueuePath());
  const cases = data?.items ?? [];

  return (
    <Panel
      eyebrow="Needs your decision"
      count={data ? data.totalCount : null}
      actions={
        data?.totalCount ? (
          <MxButton size="sm" to="/approvals">
            Open queue
          </MxButton>
        ) : null
      }
    >
      {isLoading ? <ListSkeleton rows={3} /> : null}
      {!isLoading && error ? <ErrorState compact title="Could not load the approval queue" message={error.message} /> : null}
      {!isLoading && !error && cases.length === 0 ? (
        <EmptyState compact icon={Stamp} title="Nothing waiting on you" body="Orders above the approval threshold, and every replacement, land here." />
      ) : null}
      {!isLoading && !error && cases.length > 0 ? (
        <ul className={styles.list}>
          {cases.slice(0, 4).map(({ workOrder }) => (
            <LinkRow
              key={workOrder.id}
              to="/approvals"
              trailing={
                <>
                  <span className={styles.money}>{formatMoney(workOrder.estimatedCost)}</span>
                  <span className={styles.subtle}>waiting {timeAgo(workOrder.createdAt).replace(' ago', '')}</span>
                </>
              }
            >
              <span className={styles.rowTop}>
                <TagChip tag={workOrder.asset.assetTag} />
                <span className={styles.rowTitle}>{strategyLabel(workOrder.strategy)}</span>
              </span>
              <span className={styles.rowDesc}>{workOrder.reportDescription}</span>
              <span className={styles.rowPills}>
                {workOrder.approvalBasis.exceedsThreshold ? <Pill tone="amber">Above threshold</Pill> : null}
                {workOrder.approvalBasis.isReplacement ? <Pill tone="violet">Replacement</Pill> : null}
              </span>
            </LinkRow>
          ))}
        </ul>
      ) : null}
    </Panel>
  );
}

export default ApprovalsPanel;
