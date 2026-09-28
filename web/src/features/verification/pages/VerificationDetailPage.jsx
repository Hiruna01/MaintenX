import clsx from 'clsx';
import { FileText, HelpCircle, Wrench } from 'lucide-react';
import { useParams } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import { formatInstant } from '../../../components/ui/format';
import PageHeader from '../../../components/ui/PageHeader';
import { Panel } from '../../../components/ui/Panel';
import { StatusPill } from '../../../components/ui/Pill';
import Skeleton from '../../../components/ui/Skeleton';
import { ErrorState } from '../../../components/ui/States';
import TagChip from '../../assets/components/TagChip';
import useAuth from '../../auth/hooks/useAuth';
import { MANAGER_ROLES, hasRole } from '../../auth/services/roles';
import { LoopBackPanel, NewReportsPanel } from '../components/AftermathPanels';
import ClaimChain from '../components/ClaimChain';
import { OverduePill } from '../components/VerificationPills';
import useVerification from '../hooks/useVerification';
import { didNotHold, enumLabel } from '../services/verificationApi';
import styles from '../verification.module.css';

function DetailSkeleton() {
  return (
    <div role="status" aria-label="Loading check">
      <Skeleton width={160} height={12} style={{ marginBottom: 18 }} />
      <Skeleton width="40%" height={40} style={{ marginBottom: 28 }} />
      <Skeleton height={120} radius={20} style={{ marginBottom: 20 }} />
      <div className={styles.chainSkeleton}>
        <Skeleton height={220} radius={20} />
        <Skeleton height={220} radius={20} />
        <Skeleton height={220} radius={20} />
      </div>
    </div>
  );
}

/** The error state, with 403 and 404 told apart the way the API tells them apart. */
function VerificationError({ id, error }) {
  let title = 'Could not load this check';
  let message = error.message;
  if (error.status === 404) {
    title = 'Check not found';
    message = `There is no verification check with id ${id}.`;
  } else if (error.status === 403) {
    title = 'Not your report';
    message = "Reporters see the checks on repairs to faults they reported. This one is on somebody else's report.";
  }

  return (
    <>
      <PageHeader crumbs={[{ label: 'Verification', to: '/verifications' }, { label: `Check #${id}` }]} title={`Check #${id}`} />
      <Panel>
        <ErrorState title={title} message={message} action={<MxButton to="/verifications">Back to all checks</MxButton>} />
      </Panel>
    </>
  );
}

/** The check's own clock — each moment recorded on it, in order. A missing one is "not yet". */
function Milestones({ check }) {
  const moments = [
    { label: 'Repair completed', at: check.workOrderCompletedAt },
    { label: 'Check due', at: check.dueAt },
    { label: 'Reporter asked', at: check.processedAt },
    { label: 'Reporter answered', at: check.reporterRespondedAt },
    { label: 'Sent for review', at: check.agentQueuedAt },
  ];

  return (
    <Panel eyebrow="Timeline">
      <ol className={styles.milestones}>
        {moments.map((moment) => (
          <li key={moment.label} className={clsx(styles.milestone, moment.at && styles.milestoneDone)}>
            <span className={styles.milestoneDot} aria-hidden="true" />
            <span className={styles.milestoneLabel}>{moment.label}</span>
            <span className={styles.milestoneAt}>{moment.at ? formatInstant(moment.at) : 'Not yet'}</span>
          </li>
        ))}
      </ol>
    </Panel>
  );
}

function VerificationBody({ check }) {
  const { role } = useAuth();
  const isManager = hasRole(role, MANAGER_ROLES);

  return (
    <>
      <PageHeader
        crumbs={[{ label: 'Verification', to: '/verifications' }, { label: `Check #${check.id}` }]}
        title={check.asset.name}
        actions={
          <>
            {/* A Reporter can read no work order, so the link is a manager's. */}
            {isManager ? (
              <MxButton icon={Wrench} to={`/workorders/${check.workOrderId}`}>
                Work order #{check.workOrderId}
              </MxButton>
            ) : null}
            <MxButton icon={FileText} to={`/reports/${check.reportId}`}>
              Report #{check.reportId}
            </MxButton>
          </>
        }
      >
        <div className={styles.headerPills}>
          <span className={styles.checkId}>Check #{check.id}</span>
          <StatusPill status={check.status} label={enumLabel(check.status)} />
          {check.isOverdue ? <OverduePill status={check.status} /> : null}
          <TagChip tag={check.asset.assetTag} />
          <span className={styles.headerSub}>Due {formatInstant(check.dueAt)}</span>
        </div>
      </PageHeader>

      <section className={styles.question} aria-label="The question">
        <p className={styles.questionAsk}>
          <HelpCircle aria-hidden="true" /> Is this fixed?
        </p>
        {/* What was reported, verbatim — the fault the reporter is being asked about. */}
        <blockquote className={styles.questionQuote}>{check.reportDescription}</blockquote>
        <p className={styles.questionStatus}>
          Status <StatusPill status={check.status} label={enumLabel(check.status)} />
          <span>— set by the API from the reporter&apos;s answer, never from the agent&apos;s.</span>
        </p>
      </section>

      <ClaimChain check={check} />

      <div className={styles.detailGrid}>
        <div className={styles.column}>
          {didNotHold(check) ? <LoopBackPanel check={check} /> : null}
          {/* Other people's reports: a manager's view only. Null for a Reporter, and left out. */}
          {check.newReportsSinceCompletion ? <NewReportsPanel reports={check.newReportsSinceCompletion} /> : null}
          {!didNotHold(check) && !check.newReportsSinceCompletion ? (
            <Panel eyebrow="What happens next">
              <p className={styles.sectionLead} style={{ margin: 0 }}>
                The check&apos;s status follows the reporter&apos;s answer. If they say the fault is back, the fault
                goes round the loop again and a new work order follows.
              </p>
            </Panel>
          ) : null}
        </div>
        <aside className={styles.aside}>
          <Milestones check={check} />
        </aside>
      </div>
    </>
  );
}

/**
 * One check: the repair that was claimed, what the reporter said about it afterwards, what
 * the agent made of the two — and, when it did not hold, where the fault went next.
 *
 * Open to every signed-in role, like GET /api/verifications/{id}: WHICH checks a caller may
 * read is the API's rule, and a Reporter opening someone else's gets its 403 rendered.
 */
export function VerificationDetailPage() {
  const { id } = useParams();
  const { data, isLoading, error } = useVerification(id);

  return (
    <section className={styles.page}>
      {isLoading ? <DetailSkeleton /> : null}
      {!isLoading && (error || !data) ? <VerificationError id={id} error={error ?? { message: 'Nothing came back.' }} /> : null}
      {!isLoading && !error && data ? <VerificationBody check={data} /> : null}
    </section>
  );
}

export default VerificationDetailPage;
