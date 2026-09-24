import { Link, useParams } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';
import useAuth from '../../auth/hooks/useAuth';
import { MANAGER_ROLES, hasRole } from '../../auth/services/roles';
import AgentDecisionPanel from '../components/AgentDecisionPanel';
import LoopBackPanel from '../components/LoopBackPanel';
import NewReportsPanel from '../components/NewReportsPanel';
import OverdueFlag from '../components/OverdueFlag';
import VerificationStatusBadge from '../components/VerificationStatusBadge';
import useVerification from '../hooks/useVerification';
import { didNotHold, formatDateTime } from '../services/verificationApi';

/** The error state, with 403 and 404 told apart the way the API tells them apart. */
function VerificationError({ id, error }) {
  if (error.status === 404) {
    return <ErrorMessage title="Check not found" message={`There is no verification check with id ${id}.`} />;
  }

  if (error.status === 403) {
    return (
      <ErrorMessage
        title="Not your report"
        message="Reporters see the checks on repairs to faults they reported. This one is on somebody else's report."
      />
    );
  }

  return <ErrorMessage title="Could not load this check" message={error.message} />;
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
    <section className="page verification-detail">
      <p className="page__back">
        <Link to="/verifications">← All checks</Link>
      </p>

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading check…" /> : null}

      {!isLoading && error ? <VerificationError id={id} error={error} /> : null}

      {!isLoading && !error && data ? <VerificationBody check={data} /> : null}
    </section>
  );
}

function VerificationBody({ check }) {
  const { role } = useAuth();
  const isManager = hasRole(role, MANAGER_ROLES);

  return (
    <>
      <header className="asset-hero">
        <div className="asset-hero__main">
          <div className="asset-hero__tags">
            <span className="asset-tag asset-tag--large">Check #{check.id}</span>
            <VerificationStatusBadge status={check.status} />
            {check.isOverdue ? <OverdueFlag status={check.status} /> : null}
          </div>

          <h1>
            <Link to={`/assets/${check.asset.id}`}>{check.asset.name}</Link>
          </h1>
          <p className="asset-hero__subtitle">
            <span className="asset-tag">{check.asset.assetTag}</span> · Due {formatDateTime(check.dueAt)}
          </p>
        </div>

        <dl className="asset-hero__facts">
          <div>
            <dt>Work order</dt>
            <dd>
              {/* A Reporter can read no work order, so the link is a manager's; everyone
                  sees the claim itself below. */}
              {isManager ? (
                <Link to={`/workorders/${check.workOrderId}`}>#{check.workOrderId}</Link>
              ) : (
                `#${check.workOrderId}`
              )}
            </dd>
          </div>
          <div>
            <dt>Report</dt>
            <dd>
              <Link to={`/reports/${check.reportId}`}>#{check.reportId}</Link>
            </dd>
          </div>
          <div>
            <dt>Completed</dt>
            <dd>{formatDateTime(check.workOrderCompletedAt)}</dd>
          </div>
          <div>
            <dt>Reporter asked</dt>
            <dd>{check.processedAt ? formatDateTime(check.processedAt) : 'Not yet'}</dd>
          </div>
        </dl>
      </header>

      <section className="report-section" aria-labelledby="claim-heading">
        <header className="report-section__head">
          <div>
            <h2 id="claim-heading">What the technician did</h2>
            <p className="report-section__lead">
              The resolution note, verbatim — the claim this check is testing.
            </p>
          </div>
        </header>

        <div className="work-order-panel">
          {check.workOrderResolutionNote ? (
            <blockquote className="work-order-panel__quote verification-note">
              {check.workOrderResolutionNote}
            </blockquote>
          ) : (
            <p className="work-order-panel__note">No resolution note was recorded on this work order.</p>
          )}
        </div>
      </section>

      {/* Other people's reports: a manager's view only. A Reporter's copy carries null, and
          the section is left out rather than claiming nobody has reported anything. */}
      {check.newReportsSinceCompletion ? (
        <section className="report-section" aria-labelledby="since-heading">
          <header className="report-section__head">
            <div>
              <h2 id="since-heading">Reported since the repair</h2>
              <p className="report-section__lead">
                Faults filed against this asset after the work order was completed.
              </p>
            </div>
          </header>

          <div className="work-order-panel">
            <NewReportsPanel reports={check.newReportsSinceCompletion} />
          </div>
        </section>
      ) : null}

      <section className="report-section" aria-labelledby="answer-heading">
        <header className="report-section__head">
          <div>
            <h2 id="answer-heading">The reporter&apos;s answer</h2>
            <p className="report-section__lead">
              &ldquo;Is the problem fixed?&rdquo; — one yes or no, and an optional comment.
            </p>
          </div>
        </header>

        <ReporterAnswer check={check} />
      </section>

      <section className="report-section" aria-labelledby="agent-heading">
        <header className="report-section__head">
          <div>
            <h2 id="agent-heading">Verdict</h2>
          </div>
        </header>

        <AgentDecisionPanel check={check} />
      </section>

      {didNotHold(check) ? (
        <section className="report-section" aria-labelledby="loop-heading">
          <header className="report-section__head">
            <div>
              <h2 id="loop-heading">Where it went next</h2>
              <p className="report-section__lead">
                The repair did not hold. This is what the fault has looped back to.
              </p>
            </div>
          </header>

          <LoopBackPanel check={check} />
        </section>
      ) : null}
    </>
  );
}

/**
 * The answer, or the reason there is none. `reporterConfirmed` is null until answered — null
 * is "no answer", never "no" — and an Expired check says why it was given up on.
 */
function ReporterAnswer({ check }) {
  if (check.reporterConfirmed === null || check.reporterConfirmed === undefined) {
    let body = 'Not answered yet.';
    if (check.status === 'Pending') body = 'Not asked yet — the check is still inside its waiting period.';
    if (check.status === 'Expired') body = `Never answered. ${check.expiredReason ?? ''}`.trim();

    return (
      <div className="work-order-panel">
        <p className="work-order-panel__note">{body}</p>
      </div>
    );
  }

  return (
    <div className="work-order-panel">
      <p className={`reporter-answer reporter-answer--${check.reporterConfirmed ? 'yes' : 'no'}`}>
        {check.reporterConfirmed ? 'Yes — the problem is fixed.' : 'No — the problem is not fixed.'}
      </p>
      {check.reporterComment ? (
        <blockquote className="work-order-panel__quote">{check.reporterComment}</blockquote>
      ) : null}
      <p className="work-order-panel__note">Answered {formatDateTime(check.reporterRespondedAt)}</p>
    </div>
  );
}

export default VerificationDetailPage;
