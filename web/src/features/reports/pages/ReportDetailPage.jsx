import { Boxes, CalendarClock, Eye, Layers, MapPin, MessageCircleQuestion, RefreshCw, UserRound } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import { formatInstant, timeAgo } from '../../../components/ui/format';
import Notice from '../../../components/ui/Notice';
import PageHeader from '../../../components/ui/PageHeader';
import { Panel } from '../../../components/ui/Panel';
import { Pill, StatusPill } from '../../../components/ui/Pill';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import { humanize, toneFor } from '../../../components/ui/tones';
import TagChip from '../../assets/components/TagChip';
import useAuth from '../../auth/hooks/useAuth';
import { DISPATCH_ROLES, MANAGER_ROLES, hasRole } from '../../auth/services/roles';
import RaiseWorkOrderPanel from '../../workorders/components/RaiseWorkOrderPanel';
import ClarificationList from '../components/ClarificationList';
import ReasoningPanel from '../components/ReasoningPanel';
import ReportPhoto from '../components/ReportPhoto';
import ReportStatusControl from '../components/ReportStatusControl';
import useReport from '../hooks/useReport';
import { latestAgentRunState } from '../services/agentSteps';
import { roomLabel } from '../services/reportsApi';
import styles from '../reports.module.css';

/** What an empty question list means depends on whether, and how, the clarifier ran. */
const EMPTY_CLARIFICATION = {
  none: {
    title: 'Not clarified yet',
    body: 'Questions appear here once the clarifier has run on this report.',
  },
  failed: {
    title: 'The clarifier could not run',
    body: 'Its last run failed, so nothing was asked. The reason is in the agent reasoning below.',
  },
  ok: {
    title: 'No questions were needed',
    body: 'The clarifier asks only what would change what a technician does, and this report already said it.',
  },
};

function DetailSkeleton() {
  return (
    <div role="status" aria-label="Loading report">
      <Skeleton width={160} height={12} style={{ marginBottom: 18 }} />
      <Skeleton width="38%" height={40} style={{ marginBottom: 28 }} />
      <Skeleton height={180} radius={20} style={{ marginBottom: 20 }} />
      <div className={styles.detailGrid}>
        <div className={styles.column}>
          <Skeleton height={200} radius={20} />
          <Skeleton height={320} radius={20} />
        </div>
        <Skeleton height={280} radius={20} />
      </div>
    </div>
  );
}

function crumbsFor(isManager, id) {
  return isManager
    ? [{ label: 'Reports', to: '/reports' }, { label: `#${id}` }]
    : [{ label: 'Dashboard', to: '/dashboard' }, { label: `Report #${id}` }];
}

/** The error state, with 403 and 404 told apart the way the API tells them apart. */
function ReportError({ id, error, isManager }) {
  let title = 'Could not load this report';
  let message = error.message;
  if (error.status === 404) {
    title = 'Report not found';
    message = `There is no report with id ${id}.`;
  } else if (error.status === 403) {
    // The API knows who you are and is refusing: a Reporter reads the reports they filed.
    title = 'Not your report';
    message = 'Reporters see the reports they filed. This one was filed by somebody else.';
  }

  return (
    <>
      <PageHeader crumbs={crumbsFor(isManager, id)} title={`Report #${id}`} />
      <Panel>
        <ErrorState title={title} message={message} />
      </Panel>
    </>
  );
}

/**
 * The report itself. Split out of the page so "Refresh" can remount it with a new key: a
 * fresh mount is a fresh useFetch, which is the whole of the refetch.
 */
function ReportDetail({ id, onRefresh }) {
  const { data, isLoading, error } = useReport(id);
  const { role } = useAuth();
  const isManager = hasRole(role, MANAGER_ROLES);
  // Raising an order is FacilitiesManager only, like POST /api/workorders — an Admin reads
  // the report but is not offered a control the API would refuse.
  const canDispatch = hasRole(role, DISPATCH_ROLES);

  if (isLoading) return <DetailSkeleton />;
  if (error || !data) return <ReportError id={id} error={error ?? { message: 'Nothing came back.' }} isManager={isManager} />;
  return <ReportBody report={data} isManager={isManager} canDispatch={canDispatch} onRefresh={onRefresh} />;
}

function Fact({ icon: Icon, label, children }) {
  return (
    <div className={styles.fact}>
      <dt>
        <Icon aria-hidden="true" strokeWidth={1.7} />
        {label}
      </dt>
      <dd>{children}</dd>
    </div>
  );
}

/**
 * Where the order landed, in the API's words: its status says which side of the approval
 * gate it is on, and nothing here compares the estimate with the threshold.
 */
function RaisedNotice({ order }) {
  const link = <Link to={`/workorders/${order.id}`}>Work order #{order.id}</Link>;
  const verb = order.resubmitted ? 'resubmitted' : 'raised';

  if (order.status === 'AwaitingApproval') {
    return (
      <Notice>
        {link} {verb}. It needs a manager&apos;s decision and is waiting in the{' '}
        <Link to="/approvals">approval queue</Link>.
      </Notice>
    );
  }

  if (order.status === 'Approved') {
    return (
      <Notice>
        {link} {verb} and approved — no decision was needed. Assign a technician and book a visit next.
      </Notice>
    );
  }

  // Resubmitted, but the order could not be read back to say where the gate put it.
  return <Notice>{link} {verb}. Open it to see where the approval gate routed it.</Notice>;
}

function ReportBody({ report, isManager, canDispatch, onRefresh }) {
  // The order just raised from this page. Kept here, not refetched: the panel goes the moment
  // the order exists, and the notice says where it went.
  const [raisedOrder, setRaisedOrder] = useState(null);
  const offerRaise = canDispatch && !raisedOrder && Boolean(report.latestWorkflow?.canRaiseWorkOrder);
  // An order sent back for revision is resubmitted, never joined by a second one: while the
  // Draft exists the API sets canRaiseWorkOrder false and refuses POST /api/workorders.
  const offerResubmit = canDispatch && !raisedOrder && Boolean(report.revisionDraft);

  const unanswered = report.clarificationQuestions.filter(
    (question) => question.answerText === null || question.answerText === undefined,
  ).length;
  const runState = latestAgentRunState(report.agentSteps);

  return (
    <>
      <PageHeader
        crumbs={crumbsFor(isManager, report.id)}
        title={roomLabel(report.room)}
        actions={
          <MxButton icon={RefreshCw} onClick={onRefresh}>
            Refresh
          </MxButton>
        }
      >
        <div className={styles.headerPills}>
          <span className={styles.reportId}>Report #{report.id}</span>
          <StatusPill status={report.status} />
          {/* The stage the REPORTER is shown on the phone, derived by the API — no cost, no technician. */}
          {report.stage ? (
            <Pill tone={toneFor(report.stage)} icon={Eye}>
              Reporter sees: {humanize(report.stage)}
            </Pill>
          ) : null}
          {unanswered > 0 ? (
            <Pill tone="amber" icon={MessageCircleQuestion}>
              {unanswered} unanswered
            </Pill>
          ) : null}
          <span className={styles.headerSub}>Reported {timeAgo(report.createdAt)}</span>
        </div>
      </PageHeader>

      <section className={styles.hero} aria-label="The fault as reported">
        <div className={styles.heroText}>
          <p className={styles.heroLabel}>The fault, in the reporter&apos;s words</p>
          {/* Verbatim: no truncation, no tidying. */}
          <blockquote className={styles.heroQuote}>{report.description}</blockquote>
        </div>
        <ReportPhoto url={report.photoUrl} />
      </section>

      {raisedOrder ? <RaisedNotice order={raisedOrder} /> : null}

      <div className={styles.detailGrid}>
        <div className={styles.column}>
          {offerRaise ? <RaiseWorkOrderPanel report={report} onRaised={setRaisedOrder} /> : null}
          {offerResubmit ? (
            <RaiseWorkOrderPanel report={report} revisionDraft={report.revisionDraft} onRaised={setRaisedOrder} />
          ) : null}

          <Panel eyebrow="Clarification" count={report.clarificationQuestions.length || null}>
            <p className={styles.sectionLead}>
              What the clarifier asked, and the reporter&apos;s answers. Each question is one bounded control and takes one
              answer — there is no conversation.
            </p>
            {report.clarificationQuestions.length === 0 ? (
              <EmptyState compact icon={MessageCircleQuestion} title={EMPTY_CLARIFICATION[runState].title} body={EMPTY_CLARIFICATION[runState].body} />
            ) : (
              <ClarificationList questions={report.clarificationQuestions} />
            )}
          </Panel>

          <ReasoningPanel steps={report.agentSteps} canOpenWorkflows={isManager} onRefresh={onRefresh} />
        </div>

        <aside className={styles.aside}>
          <Panel eyebrow="Details">
            <dl className={styles.facts}>
              <Fact icon={MapPin} label="Room">
                {roomLabel(report.room)}
              </Fact>
              <Fact icon={Layers} label="Floor">
                <span className="mx-mono">{report.room.floor}</span>
              </Fact>
              <Fact icon={Boxes} label="Asset">
                {report.asset ? (
                  <Link to={`/assets/${report.asset.id}`} className={styles.assetLink}>
                    <TagChip tag={report.asset.assetTag} />
                    <span>{report.asset.name}</span>
                  </Link>
                ) : (
                  // Null is the normal case: a reporter is not expected to know the tag.
                  <span className={styles.muted}>Not identified yet</span>
                )}
              </Fact>
              <Fact icon={UserRound} label="Reporter">
                User #{report.reporterId}
              </Fact>
              <Fact icon={CalendarClock} label="Reported">
                <span className="mx-mono">{formatInstant(report.createdAt)}</span>
              </Fact>
              <Fact icon={CalendarClock} label="Last updated">
                <span className="mx-mono">{formatInstant(report.updatedAt)}</span>
              </Fact>
            </dl>
          </Panel>

          {/* FacilitiesManager only, like the PATCH policy — an Admin is refused there too. */}
          {canDispatch ? <ReportStatusControl report={report} onChanged={onRefresh} /> : null}
        </aside>
      </div>
    </>
  );
}

/**
 * One report: the fault as reported, the clarification exchange, and the agent reasoning.
 *
 * Open to every signed-in role, like GET /api/reports/{id}: who may read WHICH report is
 * decided by ReportService from the token, and a Reporter asking for someone else's gets the
 * API's 403 rendered as such. The client does not keep a second copy of that rule.
 */
export function ReportDetailPage() {
  const { id } = useParams();
  const [version, setVersion] = useState(0);

  return (
    <section className={styles.page}>
      <ReportDetail key={`${id}-${version}`} id={id} onRefresh={() => setVersion((current) => current + 1)} />
    </section>
  );
}

export default ReportDetailPage;
