import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';
import useAuth from '../../auth/hooks/useAuth';
import { DISPATCH_ROLES, ROLES, hasRole } from '../../auth/services/roles';
import ApprovalBasisStatement from '../components/ApprovalBasisStatement';
import AssignTechnicianForm from '../components/AssignTechnicianForm';
import CompletionForm from '../components/CompletionForm';
import SlotFinder from '../components/SlotFinder';
import StrategyBadge from '../components/StrategyBadge';
import WorkOrderStatusBadge from '../components/WorkOrderStatusBadge';
import useWorkOrder from '../hooks/useWorkOrder';
import {
  ACTIVE_STATUSES,
  formatDateTime,
  formatMoney,
  formatSlot,
} from '../services/workOrdersApi';

/** The error state, with 403 and 404 told apart the way the API tells them apart. */
function WorkOrderError({ id, error }) {
  if (error.status === 404) {
    return <ErrorMessage title="Work order not found" message={`There is no work order with id ${id}.`} />;
  }

  if (error.status === 403) {
    return (
      <ErrorMessage
        title="Not your work order"
        message="Technicians see the work orders assigned to them. This one is assigned to somebody else."
      />
    );
  }

  return <ErrorMessage title="Could not load this work order" message={error.message} />;
}

/**
 * The order itself. Split out of the page so an action can remount it with a new key: a
 * fresh mount is a fresh useFetch, which is the whole of the refetch — the same pattern as
 * the report detail's Refresh.
 */
function WorkOrderDetail({ id, notice, onChanged }) {
  const { data, isLoading, error } = useWorkOrder(id);

  return (
    <section className="page work-order-detail">
      <p className="page__back">
        <Link to="/workorders">← Dispatch board</Link>
      </p>

      {notice ? (
        <p className="notice" role="status">
          {notice}
        </p>
      ) : null}

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading work order…" /> : null}

      {!isLoading && error ? <WorkOrderError id={id} error={error} /> : null}

      {!isLoading && !error && data ? <WorkOrderBody order={data} onChanged={onChanged} /> : null}
    </section>
  );
}

function WorkOrderBody({ order, onChanged }) {
  const { user, role } = useAuth();

  // Which controls to OFFER. Every one of these is checked again by the API — the policy,
  // the order's state and, for completion, that the caller is the assigned technician.
  const isDispatcher = hasRole(role, DISPATCH_ROLES);
  const isAssignedTechnician =
    role === ROLES.Technician && order.assignedTechnician?.id === user?.id;
  const isActive = ACTIVE_STATUSES.includes(order.status);

  return (
    <>
      <header className="asset-hero">
        <div className="asset-hero__main">
          <div className="asset-hero__tags">
            <span className="asset-tag asset-tag--large">Work order #{order.id}</span>
            <WorkOrderStatusBadge status={order.status} />
            <StrategyBadge strategy={order.strategy} />
          </div>

          <h1>
            <Link to={`/assets/${order.asset.id}`}>{order.asset.name}</Link>
          </h1>
          <p className="asset-hero__subtitle">
            <span className="asset-tag">{order.asset.assetTag}</span> · Raised{' '}
            {formatDateTime(order.createdAt)} ·{' '}
            <Link to={`/reports/${order.reportId}`}>Report #{order.reportId}</Link>
          </p>

          {/* What was complained about, verbatim, so nobody has to open the report for it. */}
          <blockquote className="report-hero__description">{order.reportDescription}</blockquote>
        </div>

        <dl className="asset-hero__facts">
          <div>
            <dt>Estimate</dt>
            <dd>{formatMoney(order.estimatedCost)}</dd>
          </div>
          <div>
            <dt>Actual</dt>
            <dd>{order.actualCost === null ? 'Not completed' : formatMoney(order.actualCost)}</dd>
          </div>
          <div>
            <dt>Parts</dt>
            <dd>{order.partsRequired ?? 'None listed'}</dd>
          </div>
          <div>
            <dt>Last updated</dt>
            <dd>{formatDateTime(order.updatedAt)}</dd>
          </div>
        </dl>
      </header>

      <section className="report-section" aria-labelledby="approval-heading">
        <header className="report-section__head">
          <div>
            <h2 id="approval-heading">Approval</h2>
            <p className="report-section__lead">
              Whether this needed a manager is decided by the API when the order is raised —
              the estimate against the threshold, and whether it replaces equipment.
            </p>
          </div>
        </header>

        <ApprovalBasisStatement estimatedCost={order.estimatedCost} basis={order.approvalBasis} compact />
        <ApprovalOutcome order={order} isDispatcher={isDispatcher} />
      </section>

      <section className="report-section" aria-labelledby="assignment-heading">
        <header className="report-section__head">
          <div>
            <h2 id="assignment-heading">Assignment</h2>
            <p className="report-section__lead">Who is going. Assigning books no time.</p>
          </div>
        </header>

        <div className="work-order-panel">
          <p className="work-order-panel__value">
            {order.assignedTechnician ? (
              <>
                {order.assignedTechnician.fullName}{' '}
                <a href={`mailto:${order.assignedTechnician.email}`}>{order.assignedTechnician.email}</a>
              </>
            ) : (
              <span className="work-order-table__unassigned">Nobody assigned yet</span>
            )}
          </p>

          {isDispatcher && isActive ? <AssignTechnicianForm order={order} onAssigned={onChanged} /> : null}

          {isDispatcher && !isActive && order.status !== 'Completed' ? (
            <p className="work-order-panel__note">
              An order can be assigned once it has cleared approval, and until it is finished.
            </p>
          ) : null}
        </div>
      </section>

      <section className="report-section" aria-labelledby="schedule-heading">
        <header className="report-section__head">
          <div>
            <h2 id="schedule-heading">Scheduling</h2>
            <p className="report-section__lead">
              Booked visits, and free time to book another. Availability is worked out by the
              API against the room&apos;s timetable and the technician&apos;s other visits.
            </p>
          </div>
        </header>

        <div className="work-order-panel">
          <p className="approval-panel__label">Booked visits</p>
          {order.scheduledSlots.length === 0 ? (
            <p className="work-order-panel__note">No visit booked yet.</p>
          ) : (
            <ol className="slot-list">
              {order.scheduledSlots.map((slot, index) => (
                <li key={slot.id} className="slot-list__item">
                  <span className="slot-list__time">{formatSlot(slot)}</span>
                  <span className="slot-list__meta">
                    Visit {index + 1} · booked {formatDateTime(slot.createdAt)}
                  </span>
                </li>
              ))}
            </ol>
          )}

          {isDispatcher && isActive ? (
            <>
              <p className="approval-panel__label">Find a time</p>
              <SlotFinder order={order} onBooked={onChanged} />
            </>
          ) : null}
        </div>
      </section>

      <section className="report-section" aria-labelledby="completion-heading">
        <header className="report-section__head">
          <div>
            <h2 id="completion-heading">Completion</h2>
            <p className="report-section__lead">
              Completing appends a record to the asset&apos;s service history and asks the
              reporter, a few days later, whether the fix held.
            </p>
          </div>
        </header>

        <CompletionSection
          order={order}
          canComplete={isAssignedTechnician && isActive}
          onCompleted={onChanged}
        />
      </section>
    </>
  );
}

/**
 * Who decided, and which way — or that nobody had to. A null approver on an order past
 * approval means it was under the threshold and auto-approved: null there is "nobody
 * decided", never "still waiting". The status is what says it is waiting.
 */
function ApprovalOutcome({ order, isDispatcher }) {
  if (order.status === 'AwaitingApproval') {
    return (
      <p className="work-order-panel__note">
        Waiting for a facilities manager&apos;s decision.{' '}
        {isDispatcher ? <Link to="/approvals">Decide it in the approval queue →</Link> : null}
      </p>
    );
  }

  if (order.status === 'Rejected') {
    return (
      <p className="work-order-panel__note">
        Rejected by {order.approvedBy?.fullName ?? 'a manager'} on {formatDateTime(order.approvedAt)}.
        <span className="work-order-panel__quote">{order.rejectionReason}</span>
      </p>
    );
  }

  if (order.status === 'Draft') {
    return order.revisionNote ? (
      <p className="work-order-panel__note">
        Sent back for revision:
        <span className="work-order-panel__quote">{order.revisionNote}</span>
      </p>
    ) : (
      <p className="work-order-panel__note">A draft — not yet routed for approval.</p>
    );
  }

  if (order.approvedBy) {
    return (
      <p className="work-order-panel__note">
        Approved by {order.approvedBy.fullName} on {formatDateTime(order.approvedAt)}.
      </p>
    );
  }

  if (order.status === 'Cancelled') {
    return <p className="work-order-panel__note">Cancelled.</p>;
  }

  return (
    <p className="work-order-panel__note">
      Approved automatically when it was raised — nobody had to decide.
    </p>
  );
}

function CompletionSection({ order, canComplete, onCompleted }) {
  if (order.status === 'Completed') {
    return (
      <div className="work-order-panel">
        <dl className="completion-facts">
          <div>
            <dt>Completed</dt>
            <dd>{formatDateTime(order.completedAt)}</dd>
          </div>
          <div>
            <dt>Actual cost</dt>
            <dd>{formatMoney(order.actualCost)}</dd>
          </div>
          <div>
            <dt>Estimate</dt>
            <dd>{formatMoney(order.estimatedCost)}</dd>
          </div>
        </dl>

        <p className="approval-panel__label">What was done</p>
        {/* Verbatim — the same words are now in the asset's service history. */}
        <blockquote className="verbatim-note">{order.resolutionNote}</blockquote>

        {order.completionPhotoUrl ? (
          <p className="work-order-panel__note">
            <a href={order.completionPhotoUrl} target="_blank" rel="noreferrer">
              Completion photo ↗
            </a>
          </p>
        ) : null}

        <p className="work-order-panel__note">
          Recorded in the <Link to={`/assets/${order.asset.id}`}>asset&apos;s service history</Link>.
        </p>
      </div>
    );
  }

  if (canComplete) {
    return <CompletionForm order={order} onCompleted={onCompleted} />;
  }

  return (
    <p className="work-order-panel__note">
      Not completed yet. The technician it is assigned to completes it from this page.
    </p>
  );
}

/**
 * One work order: the fault, the approval, who is going, when, and how it ended.
 *
 * Open to every role on the dispatch board. WHICH orders a caller may read is the API's
 * rule — a Technician opening someone else's gets its 403 rendered as such — and every
 * control offered here is checked again by the API when it is used.
 */
export function WorkOrderDetailPage() {
  const { id } = useParams();
  // The notice remembers which order it was about, so following a link to another order
  // does not carry "Visit booked" along with it.
  const [state, setState] = useState({ version: 0, notice: null, noticeFor: null });

  function handleChanged(notice) {
    setState((current) => ({ version: current.version + 1, notice, noticeFor: id }));
  }

  return (
    <WorkOrderDetail
      key={`${id}-${state.version}`}
      id={id}
      notice={state.noticeFor === id ? state.notice : null}
      onChanged={handleChanged}
    />
  );
}

export default WorkOrderDetailPage;
