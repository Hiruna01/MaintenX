import { BarChart3, Boxes, MessageSquareWarning, Plus, ShieldCheck, Stamp, Workflow, Wrench } from 'lucide-react';

import { firstName, greeting } from '../../../components/ui/format';
import PageHeader from '../../../components/ui/PageHeader';
import { Pill } from '../../../components/ui/Pill';
import useFetch from '../../../hooks/useFetch';
import useAuth from '../../auth/hooks/useAuth';
import { ROLES, roleLabel } from '../../auth/services/roles';
import ApprovalsPanel from '../components/ApprovalsPanel';
import { RegistryPanel, WorkOrderPipeline } from '../components/CountPanels';
import { AwaitingAnswerPanel, LatestReportsPanel, MyJobsPanel } from '../components/ListPanels';
import { KeyMetricsPanel, RepeatFailuresPanel } from '../components/MetricsPanels';
import QuickActions from '../components/QuickActions';
import { metricsPath } from '../services/dashboardApi';
import styles from '../dashboard.module.css';

const LEADS = {
  [ROLES.FacilitiesManager]: 'Decisions waiting on you, work in flight and how repairs are holding up.',
  [ROLES.Admin]: 'The state of the registry and how the estate is being looked after.',
  [ROLES.Technician]: 'The jobs assigned to you, and where each one stands.',
  [ROLES.Reporter]: 'The faults you have reported, and the repairs waiting on your answer.',
};

function ManagerDashboard() {
  // One request feeds both panels that read the metrics.
  const metrics = useFetch(metricsPath());
  return (
    <div className={styles.grid}>
      <div className={styles.span7}>
        <ApprovalsPanel />
      </div>
      <div className={styles.span5}>
        <KeyMetricsPanel metrics={metrics} />
      </div>
      <div className={styles.span12}>
        <WorkOrderPipeline
          statuses={[
            { value: 'AwaitingApproval', label: 'Awaiting approval', tone: 'amber' },
            { value: 'Approved', label: 'Approved', tone: 'blue' },
            { value: 'Scheduled', label: 'Scheduled', tone: 'violet' },
            { value: 'InProgress', label: 'In progress', tone: 'green' },
          ]}
        />
      </div>
      <div className={styles.span7}>
        <LatestReportsPanel listTo="/reports" emptyBody="Reports filed from the phone app appear here as they arrive." />
      </div>
      <div className={`${styles.span5} ${styles.column}`}>
        <RepeatFailuresPanel metrics={metrics} />
        <QuickActions
          actions={[
            { to: '/approvals', label: 'Review approvals', icon: Stamp },
            { to: '/workorders', label: 'Open the dispatch board', icon: Wrench },
            { to: '/reports', label: 'Triage the intake queue', icon: MessageSquareWarning },
            { to: '/metrics', label: 'View all metrics', icon: BarChart3 },
          ]}
        />
      </div>
    </div>
  );
}

function AdminDashboard() {
  const metrics = useFetch(metricsPath());
  return (
    <div className={styles.grid}>
      <div className={styles.span7}>
        <RegistryPanel />
      </div>
      <div className={styles.span5}>
        <KeyMetricsPanel metrics={metrics} />
      </div>
      <div className={styles.span7}>
        <LatestReportsPanel listTo="/reports" emptyBody="Reports filed from the phone app appear here as they arrive." />
      </div>
      <div className={`${styles.span5} ${styles.column}`}>
        <RepeatFailuresPanel metrics={metrics} />
        <QuickActions
          actions={[
            { to: '/assets/new', label: 'Register an asset', icon: Plus },
            { to: '/assets', label: 'Browse the registry', icon: Boxes },
            { to: '/workflows', label: 'Watch agent workflows', icon: Workflow },
            { to: '/metrics', label: 'View all metrics', icon: BarChart3 },
          ]}
        />
      </div>
    </div>
  );
}

function TechnicianDashboard() {
  return (
    <div className={styles.grid}>
      <div className={styles.span12}>
        <WorkOrderPipeline
          title="Your jobs"
          statuses={[
            { value: 'Approved', label: 'Ready to schedule', tone: 'blue' },
            { value: 'Scheduled', label: 'Scheduled', tone: 'violet' },
            { value: 'InProgress', label: 'In progress', tone: 'amber' },
            { value: 'Completed', label: 'Completed', tone: 'green' },
          ]}
        />
      </div>
      <div className={styles.span7}>
        <MyJobsPanel />
      </div>
      <div className={styles.span5}>
        <QuickActions
          actions={[
            { to: '/workorders', label: 'Open your job board', icon: Wrench },
            { to: '/assets', label: 'Look up a machine', icon: Boxes },
          ]}
        />
      </div>
    </div>
  );
}

function ReporterDashboard() {
  return (
    <div className={styles.grid}>
      <div className={styles.span7}>
        <AwaitingAnswerPanel />
      </div>
      <div className={styles.span5}>
        <QuickActions
          actions={[
            { to: '/verifications', label: 'Your repair checks', icon: ShieldCheck },
            { to: '/assets', label: 'Look up a machine', icon: Boxes },
          ]}
        />
      </div>
      <div className={styles.span12}>
        <LatestReportsPanel title="Your reports" emptyBody="Report a fault from the MaintenX app on your phone and it appears here." />
      </div>
    </div>
  );
}

const BY_ROLE = {
  [ROLES.FacilitiesManager]: ManagerDashboard,
  [ROLES.Admin]: AdminDashboard,
  [ROLES.Technician]: TechnicianDashboard,
  [ROLES.Reporter]: ReporterDashboard,
};

/**
 * A role-aware overview. Every panel reads an existing GET endpoint and renders its own
 * loading, error and empty states; nothing here computes a business rule. An unknown role
 * gets the reporter's view — the narrowest one — rather than the estate.
 */
export function DashboardPage() {
  const { user } = useAuth();
  const Body = BY_ROLE[user.role] ?? ReporterDashboard;
  const today = new Date().toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' });

  return (
    <section className={styles.page}>
      <PageHeader title={`${greeting()}, ${firstName(user.fullName)}`} lead={LEADS[user.role]}>
        <div className={styles.headerMeta}>
          <Pill tone="violet">{roleLabel(user.role)}</Pill>
          <span className={styles.date}>{today}</span>
        </div>
      </PageHeader>
      <Body />
    </section>
  );
}

export default DashboardPage;
