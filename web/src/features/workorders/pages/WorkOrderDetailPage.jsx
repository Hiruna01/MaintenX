import { Boxes, CalendarDays, FileText, ImageIcon, Mail, MapPin, UserRound } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import { formatInstant, initials, timeAgo } from '../../../components/ui/format';
import Notice from '../../../components/ui/Notice';
import PageHeader from '../../../components/ui/PageHeader';
import { Panel } from '../../../components/ui/Panel';
import { StatusPill } from '../../../components/ui/Pill';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import TagChip from '../../assets/components/TagChip';
import useAuth from '../../auth/hooks/useAuth';
import { DISPATCH_ROLES, ROLES, hasRole } from '../../auth/services/roles';
import ApprovalBasisStatement from '../components/ApprovalBasisStatement';
import AssignTechnicianForm from '../components/AssignTechnicianForm';
import CompletionForm from '../components/CompletionForm';
import DiagnosisPanel from '../components/DiagnosisPanel';
import JobProgress from '../components/JobProgress';
import SlotFinder from '../components/SlotFinder';
import TimetableSyncButton from '../components/TimetableSyncButton';
import StrategyPill from '../components/StrategyPill';
import useWorkOrder from '../hooks/useWorkOrder';
import { ACTIVE_STATUSES, enumLabel, formatMoney, formatSlot } from '../services/workOrdersApi';
import styles from '../workorders.module.css';

function DetailSkeleton() {
  return (
    <div role="status" aria-label="Loading work order">
      <Skeleton width={160} height={12} style={{ marginBottom: 18 }} />
      <Skeleton width="42%" height={40} style={{ marginBottom: 28 }} />
      <div className={styles.detailGrid}>
        <div className={styles.column}>
          <Skeleton height={120} radius={20} />
          <Skeleton height={140} radius={20} />
          <Skeleton height={220} radius={20} />
        </div>
        <div className={styles.column}>
          <Skeleton height={180} radius={20} />
          <Skeleton height={260} radius={20} />
        </div>
      </div>
    </div>
  );
}

/** The error state, with 403 and 404 told apart the way the API tells them apart. */
function WorkOrderError({ id, error }) {
  const title = error.status === 404 ? 'Work order not found' : error.status === 403 ? 'Not your work order' : 'Could not load this work order';
  const message =
    error.status === 404
      ? `There is no work order with id ${id}.`
      : error.status === 403
        ? 'Technicians see the work orders assigned to them. This one is assigned to somebody else.'
        : error.message;

  return (
    <>
      <PageHeader crumbs={[{ label: 'Work orders', to: '/workorders' }, { label: `#${id}` }]} title={`Work order #${id}`} />
      <Panel>
        <ErrorState title={title} message={message} action={<MxButton to="/workorders">Back to the board</MxButton>} />
      </Panel>
    </>
  );
}

/**
 * The order itself. Split out of the page so an action can remount it with a new key: a
 * fresh mount is a fresh useFetch, which is the whole of the refetch.
 */
function WorkOrderDetail({ id, notice, onChanged }) {
  const { data, isLoading, error } = useWorkOrder(id);

  if (isLoading) return <DetailSkeleton />;
  if (error || !data) return <WorkOrderError id={id} error={error ?? { message: 'Nothing came back.' }} />;
  return <WorkOrderBody order={data} notice={notice} onChanged={onChanged} />;
}

function Section({ eyebrow, lead, children, actions }) {
  return (
    <Panel eyebrow={eyebrow} actions={actions}>
      {lead ? <p className={styles.sectionLead}>{lead}</p> : null}
      {children}
    </Panel>
  );
}

function WorkOrderBody({ order, notice, onChanged }) {
  const { user, role } = useAuth();

  // Which controls to OFFER. Every one of these is checked again by the API — the policy,
  // the order's state and, for completion, that the caller is the assigned technician.
  const isDispatcher = hasRole(role, DISPATCH_ROLES);
  const isAssignedTechnician = role === ROLES.Technician && order.assignedTechnician?.id === user?.id;
  const isActive = ACTIVE_STATUSES.includes(order.status);
  const [slotFinderKey, setSlotFinderKey] = useState(0);

  return (
    <>
      <PageHeader
        crumbs={[{ label: 'Work orders', to: '/workorders' }, { label: `#${order.id}` }]}
        title={order.asset.name}
        actions={
          <>
            <MxButton icon={Boxes} to={`/assets/${order.asset.id}`}>
              Asset
            </MxButton>
            <MxButton icon={FileText} to={`/reports/${order.reportId}`}>
              Report #{order.reportId}
            </MxButton>
          </>
        }
      >
        <div className={styles.headerTags}>
          <TagChip tag={order.asset.assetTag} />
          <StatusPill status={order.status} label={enumLabel(order.status)} />
          <StrategyPill strategy={order.strategy} />
        </div>
        <p className={styles.headerMeta}>
          <span>
            <MapPin aria-hidden="true" size={13} /> {order.room.code} · {order.room.name} · floor {order.room.floor}
          </span>
          <span>
            Work order <span className="mx-mono">#{order.id}</span> · raised {timeAgo(order.createdAt)}
          </span>
        </p>
      </PageHeader>

      <Notice>{notice}</Notice>

      <div className={styles.detailGrid}>
        <div className={styles.column}>
          <JobProgress order={order} />

          <Panel eyebrow="Reported fault">
            {/* What was complained about, verbatim, so nobody has to open the report for it. */}
            <blockquote className={styles.fault}>{order.reportDescription}</blockquote>
          </Panel>

          <Section eyebrow="Approval" lead="Whether this needed a manager is decided by the API when the order is raised — the estimate against the threshold, and whether it replaces equipment.">
            <ApprovalBasisStatement estimatedCost={order.estimatedCost} basis={order.approvalBasis} />
            <ApprovalOutcome order={order} isDispatcher={isDispatcher} />
          </Section>

          <Section eyebrow="Assignment" lead="Who is going. Assigning books no time.">
            {order.assignedTechnician ? (
              <div className={styles.personCard}>
                <span className={styles.personAvatarLarge} aria-hidden="true">
                  {initials(order.assignedTechnician.fullName)}
                </span>
                <div>
                  <p className={styles.personName}>{order.assignedTechnician.fullName}</p>
                  <a className={styles.personEmail} href={`mailto:${order.assignedTechnician.email}`}>
                    <Mail aria-hidden="true" size={13} /> {order.assignedTechnician.email}
                  </a>
                </div>
              </div>
            ) : (
              <p className={styles.unassignedLine}>
                <UserRound aria-hidden="true" /> Nobody assigned yet
              </p>
            )}

            {isDispatcher && isActive ? <AssignTechnicianForm order={order} onAssigned={onChanged} /> : null}

            {isDispatcher && !isActive && order.status !== 'Completed' ? (
              <p className={styles.note}>An order can be assigned once it has cleared approval, and until it is finished.</p>
            ) : null}
          </Section>

          <Section
            eyebrow="Scheduling"
            lead="Booked visits, and free time to book another. Availability is worked out by the API against the room's timetable and the technician's other visits."
          >
            <p className={styles.miniLabel}>Booked visits</p>
            {order.scheduledSlots.length === 0 ? (
              <p className={styles.note}>No visit booked yet.</p>
            ) : (
              <ol className={styles.visits}>
                {order.scheduledSlots.map((slot, index) => (
                  <li key={slot.id}>
                    <CalendarDays aria-hidden="true" />
                    <span className={styles.visitTime}>{formatSlot(slot)}</span>
                    <span className={styles.visitMeta}>
                      Visit {index + 1} · booked {formatInstant(slot.createdAt)}
                    </span>
                  </li>
                ))}
              </ol>
            )}

            {isDispatcher && isActive ? (
              <>
                <p className={styles.miniLabel} style={{ marginTop: '1.25rem' }}>
                  Find a time
                </p>
                <TimetableSyncButton onSynced={() => setSlotFinderKey((key) => key + 1)} />
                {/* Remounted after a sync, so results found against the old timetable are cleared. */}
                <SlotFinder key={slotFinderKey} order={order} onBooked={onChanged} />
              </>
            ) : null}
          </Section>

          <Section
            eyebrow="Completion"
            lead="Completing appends a record to the asset's service history and asks the reporter, a few days later, whether the fix held."
          >
            <CompletionSection order={order} canComplete={isAssignedTechnician && isActive} onCompleted={onChanged} />
          </Section>
        </div>

        <aside className={styles.aside}>
          <Panel eyebrow="Cost">
            <p className={styles.bigMoney}>{formatMoney(order.estimatedCost)}</p>
            <p className={styles.bigMoneyLabel}>Estimate</p>
            <dl className={styles.facts}>
              <div>
                <dt>Actual</dt>
                <dd className="mx-mono">{order.actualCost === null ? 'Not completed' : formatMoney(order.actualCost)}</dd>
              </div>
              <div>
                <dt>Parts</dt>
                <dd>{order.partsRequired ?? 'None listed'}</dd>
              </div>
              <div>
                <dt>Last updated</dt>
                <dd className="mx-mono">{formatInstant(order.updatedAt)}</dd>
              </div>
            </dl>
          </Panel>

          {/* The same reading the approval queue shows. Null is "never diagnosed", not failed. */}
          <DiagnosisPanel diagnosis={order.diagnosis} reportId={order.reportId} />
        </aside>
      </div>
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
      <p className={styles.note}>
        Waiting for a facilities manager&apos;s decision.{' '}
        {isDispatcher ? <Link to="/approvals">Decide it in the approval queue →</Link> : null}
      </p>
    );
  }

  if (order.status === 'Rejected') {
    return (
      <div className={styles.note}>
        Rejected by {order.approvedBy?.fullName ?? 'a manager'} on {formatInstant(order.approvedAt)}.
        <blockquote className={styles.quote}>{order.rejectionReason}</blockquote>
      </div>
    );
  }

  if (order.status === 'Draft') {
    return order.revisionNote ? (
      <div className={styles.note}>
        Sent back for revision:
        <blockquote className={styles.quote}>{order.revisionNote}</blockquote>
      </div>
    ) : (
      <p className={styles.note}>A draft — not yet routed for approval.</p>
    );
  }

  if (order.approvedBy) {
    return (
      <p className={styles.note}>
        Approved by {order.approvedBy.fullName} on {formatInstant(order.approvedAt)}.
      </p>
    );
  }

  if (order.status === 'Cancelled') return <p className={styles.note}>Cancelled.</p>;

  return <p className={styles.note}>Approved automatically when it was raised — nobody had to decide.</p>;
}

function CompletionSection({ order, canComplete, onCompleted }) {
  if (order.status === 'Completed') {
    return (
      <>
        <dl className={styles.completionFacts}>
          <div>
            <dt>Completed</dt>
            <dd className="mx-mono">{formatInstant(order.completedAt)}</dd>
          </div>
          <div>
            <dt>Actual cost</dt>
            <dd className="mx-mono">{formatMoney(order.actualCost)}</dd>
          </div>
          <div>
            <dt>Estimate</dt>
            <dd className="mx-mono">{formatMoney(order.estimatedCost)}</dd>
          </div>
        </dl>

        <p className={styles.miniLabel}>What was done</p>
        {/* Verbatim — the same words are now in the asset's service history. */}
        <blockquote className={styles.fault}>{order.resolutionNote}</blockquote>

        <p className={styles.note}>
          {order.completionPhotoUrl ? (
            <>
              <a href={order.completionPhotoUrl} target="_blank" rel="noreferrer">
                <ImageIcon aria-hidden="true" size={13} /> Completion photo ↗
              </a>{' '}
              ·{' '}
            </>
          ) : null}
          Recorded in the <Link to={`/assets/${order.asset.id}`}>asset&apos;s service history</Link>.
        </p>
      </>
    );
  }

  if (canComplete) return <CompletionForm order={order} onCompleted={onCompleted} />;

  return (
    <EmptyState compact title="Not completed yet" body="The technician it is assigned to completes it from this page." />
  );
}

/**
 * One work order: the fault, the approval, who is going, when, and how it ended.
 *
 * WHICH orders a caller may read is the API's rule — a Technician opening someone else's
 * gets its 403 rendered as such — and every control offered here is checked again by the
 * API when it is used.
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
    <section className={styles.page}>
      <WorkOrderDetail
        key={`${id}-${state.version}`}
        id={id}
        notice={state.noticeFor === id ? state.notice : null}
        onChanged={handleChanged}
      />
    </section>
  );
}

export default WorkOrderDetailPage;
