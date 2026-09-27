import { ClipboardList, MessageSquareWarning, ShieldCheck } from 'lucide-react';

import MxButton from '../../../components/ui/Button';
import { timeAgo } from '../../../components/ui/format';
import { Panel } from '../../../components/ui/Panel';
import { Pill, StatusPill } from '../../../components/ui/Pill';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useFetch from '../../../hooks/useFetch';
import TagChip from '../../assets/components/TagChip';
import { formatMoney, strategyLabel } from '../../workorders/services/workOrdersApi';
import { awaitingAnswerPath, latestReportsPath, latestWorkOrdersPath } from '../services/dashboardApi';
import styles from '../dashboard.module.css';
import { LinkRow, ListSkeleton } from './parts';

/** The newest reports the caller may see — the estate for a manager, their own for a reporter. */
export function LatestReportsPanel({ title = 'Latest reports', listTo, emptyBody }) {
  const { data, isLoading, error } = useFetch(latestReportsPath());
  const reports = data?.items ?? [];

  return (
    <Panel
      eyebrow={title}
      count={data ? data.totalCount : null}
      actions={
        listTo ? (
          <MxButton size="sm" variant="ghost" to={listTo}>
            View all
          </MxButton>
        ) : null
      }
    >
      {isLoading ? <ListSkeleton rows={4} /> : null}
      {!isLoading && error ? <ErrorState compact title="Could not load reports" message={error.message} /> : null}
      {!isLoading && !error && reports.length === 0 ? (
        <EmptyState compact icon={MessageSquareWarning} title="No reports yet" body={emptyBody} />
      ) : null}
      {!isLoading && !error && reports.length > 0 ? (
        <ul className={styles.list}>
          {reports.map((report) => (
            <LinkRow
              key={report.id}
              to={`/reports/${report.id}`}
              trailing={
                <>
                  <StatusPill status={report.status} />
                  <span className={styles.subtle}>{timeAgo(report.createdAt)}</span>
                </>
              }
            >
              <span className={styles.rowTitle}>{report.description}</span>
              <span className={styles.rowDesc}>
                {report.roomName}
                {report.unansweredQuestionCount > 0 ? ` · ${report.unansweredQuestionCount} unanswered` : ''}
              </span>
            </LinkRow>
          ))}
        </ul>
      ) : null}
    </Panel>
  );
}

/** A technician's newest assigned orders — the API scopes the list to them. */
export function MyJobsPanel() {
  const { data, isLoading, error } = useFetch(latestWorkOrdersPath());
  const orders = data?.items ?? [];

  return (
    <Panel
      eyebrow="Your latest jobs"
      count={data ? data.totalCount : null}
      actions={
        <MxButton size="sm" variant="ghost" to="/workorders">
          View board
        </MxButton>
      }
    >
      {isLoading ? <ListSkeleton rows={4} /> : null}
      {!isLoading && error ? <ErrorState compact title="Could not load your jobs" message={error.message} /> : null}
      {!isLoading && !error && orders.length === 0 ? (
        <EmptyState compact icon={ClipboardList} title="Nothing assigned to you" body="Jobs appear here once a manager assigns them to you." />
      ) : null}
      {!isLoading && !error && orders.length > 0 ? (
        <ul className={styles.list}>
          {orders.map((order) => (
            <LinkRow
              key={order.id}
              to={`/workorders/${order.id}`}
              trailing={
                <>
                  <StatusPill status={order.status} />
                  <span className={styles.subtle}>{formatMoney(order.estimatedCost)}</span>
                </>
              }
            >
              <span className={styles.rowTop}>
                <TagChip tag={order.assetTag} />
              </span>
              <span className={styles.rowTitle}>{strategyLabel(order.strategy)}</span>
              <span className={styles.rowDesc}>Raised {timeAgo(order.createdAt)}</span>
            </LinkRow>
          ))}
        </ul>
      ) : null}
    </Panel>
  );
}

/** Repair checks waiting on this reporter's answer. Overdue is the API's `isOverdue`. */
export function AwaitingAnswerPanel() {
  const { data, isLoading, error } = useFetch(awaitingAnswerPath());
  const checks = data?.items ?? [];

  return (
    <Panel eyebrow="Was it fixed?" count={data ? data.totalCount : null}>
      <p className={styles.panelLead}>
        A few days after a repair we ask whether it held. Answer these in the MaintenX app on your phone.
      </p>
      {isLoading ? <ListSkeleton rows={2} /> : null}
      {!isLoading && error ? <ErrorState compact title="Could not load your repair checks" message={error.message} /> : null}
      {!isLoading && !error && checks.length === 0 ? (
        <EmptyState
          compact
          icon={ShieldCheck}
          title="No checks waiting"
          body="When a repair on one of your reports is due for a check, it appears here."
        />
      ) : null}
      {!isLoading && !error && checks.length > 0 ? (
        <ul className={styles.list}>
          {checks.map((check) => (
            <LinkRow
              key={check.id}
              to={`/verifications/${check.id}`}
              trailing={check.isOverdue ? <Pill tone="red">Overdue</Pill> : <Pill tone="amber">Waiting</Pill>}
            >
              <span className={styles.rowTop}>
                <TagChip tag={check.assetTag} />
              </span>
              <span className={styles.rowTitle}>{check.reportDescription}</span>
              <span className={styles.rowDesc}>
                Repaired {check.workOrderCompletedAt ? timeAgo(check.workOrderCompletedAt) : '—'}
              </span>
            </LinkRow>
          ))}
        </ul>
      ) : null}
    </Panel>
  );
}
