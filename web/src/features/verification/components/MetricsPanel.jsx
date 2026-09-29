import { ChartNoAxesColumn } from 'lucide-react';

import { Panel } from '../../../components/ui/Panel';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import styles from '../verification.module.css';

/**
 * One metrics panel and its four states — loading, error, empty and data. Every chart on the
 * page goes through this, so none can render an empty area: an empty chart looks like a broken
 * one, and "not enough data yet" is a different fact from both a failure and a zero.
 */
export function MetricsPanel({ title, lead, isLoading, error, isEmpty, emptyBody, children, skeletonHeight = 240 }) {
  return (
    <Panel eyebrow={title} className={styles.metricsPanel}>
      {lead ? <p className={styles.sectionLead}>{lead}</p> : null}

      {isLoading ? <Skeleton height={skeletonHeight} radius={14} /> : null}

      {!isLoading && error ? <ErrorState compact title={`Could not load ${title.toLowerCase()}`} message={error.message} /> : null}

      {!isLoading && !error && isEmpty ? (
        <EmptyState compact icon={ChartNoAxesColumn} title="Not enough data yet" body={emptyBody} />
      ) : null}

      {!isLoading && !error && !isEmpty ? children : null}
    </Panel>
  );
}

export default MetricsPanel;
