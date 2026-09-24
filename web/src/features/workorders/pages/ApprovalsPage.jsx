import { useState } from 'react';

import ErrorMessage from '../../../components/ErrorMessage';
import Pagination from '../../../components/Pagination';
import Spinner from '../../../components/Spinner';
import ApprovalCard from '../components/ApprovalCard';
import useApprovalQueue from '../hooks/useApprovalQueue';

/**
 * The approval queue — every work order waiting on a facilities manager, oldest first.
 *
 * Each card carries everything the decision needs: the fault as reported, the estimate
 * against the threshold in plain words, the agent's proposal beside the order as raised, the
 * diagnosis with its evidence, and the machine's full service history and failure summary.
 * One request brings all of it (GET /api/workorders/approvals), so a manager decides without
 * opening anything else.
 *
 * Nothing on this page decides anything. Whether an order needs approval is the API's
 * `approvalBasis`; the diagnosis and proposal are advice; and what Approve, Reject and
 * Request revision DO is C# in WorkOrderService. FacilitiesManager only — the route guard
 * and the nav both mirror the API's policy, so a Technician never sees a link to it.
 */
function ApprovalQueue({ page, onPageChange, notice, onDecided }) {
  const { data, isLoading, error } = useApprovalQueue(page);

  return (
    <>
      {notice ? (
        <p className="notice" role="status">
          {notice}
        </p>
      ) : null}

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading the approval queue…" /> : null}

      {!isLoading && error ? (
        <ErrorMessage title="Could not load the approval queue" message={error.message} />
      ) : null}

      {!isLoading && !error && data ? (
        <>
          {data.items.length === 0 ? (
            // An empty queue is good news, not a failure.
            <div className="empty-state">
              <p className="empty-state__title">Nothing is waiting for a decision</p>
              <p className="empty-state__body">
                Orders estimated above the approval threshold, and every replacement, land here
                when they are raised.
              </p>
            </div>
          ) : (
            <>
              <p className="approval-queue__count">
                {data.totalCount} {data.totalCount === 1 ? 'order is' : 'orders are'} waiting ·
                longest-waiting first
              </p>
              <div className="approval-queue">
                {data.items.map((item) => (
                  <ApprovalCard key={item.workOrder.id} item={item} onDecided={onDecided} />
                ))}
              </div>
            </>
          )}

          {data.totalCount > 0 ? (
            <Pagination
              page={data.page}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={onPageChange}
            />
          ) : null}
        </>
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
    <section className="page">
      <header className="page-header">
        <div>
          <p className="page-header__eyebrow">Facilities manager</p>
          <h1>Approvals</h1>
          <p className="page__lead">
            Work orders that need your decision before any work is booked. Everything the
            decision needs is on each card.
          </p>
        </div>
      </header>

      <ApprovalQueue
        key={state.version}
        page={page}
        onPageChange={setPage}
        notice={state.notice}
        onDecided={handleDecided}
      />
    </section>
  );
}

export default ApprovalsPage;
