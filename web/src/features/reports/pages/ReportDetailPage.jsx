import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';
import useAuth from '../../auth/hooks/useAuth';
import { MANAGER_ROLES, hasRole } from '../../auth/services/roles';
import AgentReasoningPanel from '../components/AgentReasoningPanel';
import ClarificationAnswers from '../components/ClarificationAnswers';
import ReportPhoto from '../components/ReportPhoto';
import ReportStatusBadge from '../components/ReportStatusBadge';
import useReport from '../hooks/useReport';
import { latestAgentRunState } from '../services/agentSteps';
import { formatDateTime, roomLabel } from '../services/reportsApi';

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

/** The error state, with 403 and 404 told apart the way the API tells them apart. */
function ReportError({ id, error }) {
  if (error.status === 404) {
    return <ErrorMessage title="Report not found" message={`There is no report with id ${id}.`} />;
  }

  if (error.status === 403) {
    // The API knows who you are and is refusing, not asking: a Reporter reads the reports
    // they filed and nobody else's.
    return (
      <ErrorMessage
        title="Not your report"
        message="Reporters see the reports they filed. This one was filed by somebody else."
      />
    );
  }

  return <ErrorMessage title="Could not load this report" message={error.message} />;
}

/**
 * The report itself. Split out of the page so "Refresh" can remount it with a new key: a
 * fresh mount is a fresh useFetch, which is the whole of the refetch — no extra machinery
 * in the shared hook.
 */
function ReportDetail({ id, onRefresh }) {
  const { data, isLoading, error } = useReport(id);
  const { role } = useAuth();
  const isManager = hasRole(role, MANAGER_ROLES);

  return (
    <section className="page report-detail">
      <p className="page__back">
        {isManager ? <Link to="/reports">← All reports</Link> : <Link to="/dashboard">← Dashboard</Link>}
      </p>

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading report…" /> : null}

      {!isLoading && error ? <ReportError id={id} error={error} /> : null}

      {!isLoading && !error && data ? (
        <ReportBody report={data} isManager={isManager} onRefresh={onRefresh} />
      ) : null}
    </section>
  );
}

function ReportBody({ report, isManager, onRefresh }) {
  const unanswered = report.clarificationQuestions.filter(
    (question) => question.answerText === null || question.answerText === undefined,
  ).length;

  const runState = latestAgentRunState(report.agentSteps);

  return (
    <>
      <header className="asset-hero report-hero">
        <div className="asset-hero__main">
          <div className="asset-hero__tags">
            <span className="asset-tag asset-tag--large">Report #{report.id}</span>
            <ReportStatusBadge status={report.status} />
            {unanswered > 0 ? (
              <span className="report-table__waiting">{unanswered} unanswered</span>
            ) : null}
          </div>

          <h1>{roomLabel(report.room)}</h1>
          <p className="asset-hero__subtitle">Reported {formatDateTime(report.createdAt)}</p>

          {/* The reporter's own words, verbatim: no truncation, no tidying. */}
          <blockquote className="report-hero__description">{report.description}</blockquote>
        </div>

        <ReportPhoto url={report.photoUrl} />

        <dl className="asset-hero__facts">
          <div>
            <dt>Floor</dt>
            <dd>{report.room.floor}</dd>
          </div>
          <div>
            <dt>Asset</dt>
            <dd>
              {report.asset ? (
                <Link to={`/assets/${report.asset.id}`}>
                  {report.asset.assetTag} · {report.asset.name}
                </Link>
              ) : (
                // Null is the normal case: a reporter is not expected to know the tag.
                'Not identified yet'
              )}
            </dd>
          </div>
          <div>
            <dt>Reporter</dt>
            <dd>User #{report.reporterId}</dd>
          </div>
          <div>
            <dt>Last updated</dt>
            <dd>{formatDateTime(report.updatedAt)}</dd>
          </div>
        </dl>
      </header>

      <section className="report-section" aria-labelledby="clarification-heading">
        <header className="report-section__head">
          <div>
            <h2 id="clarification-heading">Clarification</h2>
            <p className="report-section__lead">
              What the clarifier asked, and the reporter&apos;s answers. Each question is one
              bounded control and takes one answer — there is no conversation.
            </p>
          </div>
        </header>

        {report.clarificationQuestions.length === 0 ? (
          <div className="empty-state">
            <p className="empty-state__title">{EMPTY_CLARIFICATION[runState].title}</p>
            <p className="empty-state__body">{EMPTY_CLARIFICATION[runState].body}</p>
          </div>
        ) : (
          <ClarificationAnswers questions={report.clarificationQuestions} />
        )}
      </section>

      <AgentReasoningPanel
        steps={report.agentSteps}
        canOpenWorkflows={isManager}
        onRefresh={onRefresh}
      />
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
    <ReportDetail key={version} id={id} onRefresh={() => setVersion((current) => current + 1)} />
  );
}

export default ReportDetailPage;
