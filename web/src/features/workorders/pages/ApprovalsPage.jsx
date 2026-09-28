import { Stamp } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import Notice from '../../../components/ui/Notice';
import PageHeader from '../../../components/ui/PageHeader';
import Pager from '../../../components/ui/Pager';
import { Panel } from '../../../components/ui/Panel';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import ApprovalCard from '../components/ApprovalCard';
import useApprovalQueue from '../hooks/useApprovalQueue';
import styles from '../workorders.module.css';

function QueueSkeleton() {
  return (
    <div className={styles.column} role="status" aria-label="Loading the approval queue">
      {[0, 1].map((index) => (
        <div key={index} className={styles.caseCard}>
          <Skeleton width="40%" height={14} style={{ marginBottom: 12 }} />
          <Skeleton width="55%" height={28} style={{ marginBottom: 20 }} />
          <Skeleton height={64} radius={14} style={{ marginBottom: 16 }} />
          <div className={styles.caseGrid}>
            <Skeleton height={260} radius={20} />
            <Skeleton height={260} radius={20} />
          </div>
        </div>
      ))}
    </div>
  );
}

/**
 * The approval queue — every work order waiting on a facilities manager, oldest first.
 *
 * One request (GET /api/workorders/approvals) brings everything each decision needs, so a
 * manager decides without opening anything else. Nothing on this page decides anything:
 * whether an order needs approval is the API's `approvalBasis`, the diagnosis and proposal
 * are advice, and what Approve, Reject and Request revision DO is C# in WorkOrderService.
 */
function ApprovalQueue({ page, onPageChange, notice, onDecided }) {
  const { data, isLoading, error } = useApprovalQueue(page);
  const waiting = data?.totalCount;

  return (
    <>
      {/* The header lives in the keyed queue so its count is re-read after every decision. */}
      <PageHeader
        crumbs={[{ label: 'Operations' }, { label: 'Approvals' }]}
        title="Approvals"
        lead="Work orders that need your decision before any work is booked. Everything the decision needs is on each card — longest-waiting first."
        actions={
          typeof waiting === 'number' ? (
            <span className={styles.headerCount}>
              <strong>{waiting}</strong> {waiting === 1 ? 'order is' : 'orders are'} waiting
            </span>
          ) : null
        }
      />

      <Notice>{notice}</Notice>

      {isLoading ? <QueueSkeleton /> : null}

      {!isLoading && error ? (
        <Panel>
          <ErrorState title="Could not load the approval queue" message={error.message} />
        </Panel>
      ) : null}

      {!isLoading && !error && data ? (
        data.items.length === 0 ? (
          // An empty queue is good news, not a failure.
          <Panel>
            <EmptyState
              icon={Stamp}
              title="Nothing is waiting for a decision"
              body="Orders estimated above the approval threshold, and every replacement, land here when they are raised."
              action={<MxButton to="/workorders">Open the dispatch board</MxButton>}
            />
          </Panel>
        ) : (
          <>
            <div className={styles.column}>
              {data.items.map((item, index) => (
                <ApprovalCard key={item.workOrder.id} item={item} index={index} onDecided={onDecided} />
              ))}
            </div>
            <Pager
              page={data.page}
              pageSize={data.pageSize}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={onPageChange}
              noun={data.totalCount === 1 ? 'order' : 'orders'}
            />
          </>
        )
      ) : null}
    </>
  );
}

export function ApprovalsPage() {
  const [page, setPage] = useState(1);
  const [state, setState] = useState({ version: 0, notice: null });

  // A decision takes the order out of the queue, so the queue is fetched again — remounted
  // with a new key, which is a fresh useFetch. Back to page 1, since the pages have shifted.
  function handleDecided(notice) {
    setPage(1);
    setState((current) => ({ version: current.version + 1, notice }));
  }

  return (
    <section className={styles.page}>
      <ApprovalQueue key={state.version} page={page} onPageChange={setPage} notice={state.notice} onDecided={handleDecided} />
    </section>
  );
}

export default ApprovalsPage;
