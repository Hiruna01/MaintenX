import clsx from 'clsx';

import { Panel } from '../../../components/ui/Panel';
import useFetch from '../../../hooks/useFetch';
import { assetCountPath, workOrderCountPath } from '../services/dashboardApi';
import styles from '../dashboard.module.css';
import { CountTile } from './parts';

function countOf(result) {
  return result.isLoading || result.error ? null : result.data?.totalCount ?? null;
}

/**
 * Work orders by status — four tiles, each a page-of-one request, so the number is the API's
 * `totalCount` for this caller (a Technician's own orders, a manager's estate).
 * `statuses` is always exactly four [{ value, label, tone }].
 */
export function WorkOrderPipeline({ title = 'Work order pipeline', statuses }) {
  const first = useFetch(workOrderCountPath(statuses[0].value));
  const second = useFetch(workOrderCountPath(statuses[1].value));
  const third = useFetch(workOrderCountPath(statuses[2].value));
  const fourth = useFetch(workOrderCountPath(statuses[3].value));
  const results = [first, second, third, fourth];
  const failed = results.some((result) => result.error);

  return (
    <Panel eyebrow={title}>
      <div className={styles.tiles} style={{ '--tiles': 4 }}>
        {statuses.map((status, index) => (
          <CountTile
            key={status.value}
            label={status.label}
            tone={status.tone}
            count={results[index].error ? '—' : countOf(results[index])}
            to="/workorders"
          />
        ))}
      </div>
      {failed ? <p className={styles.caption}>Some counts could not be loaded.</p> : null}
    </Panel>
  );
}

const ASSET_STATUSES = [
  { value: 'Active', label: 'Active', tone: 'green' },
  { value: 'UnderMaintenance', label: 'Under maintenance', tone: 'amber' },
  { value: 'Retired', label: 'Retired', tone: 'slate' },
];

/** The registry by status, with a bar showing the split. Counts are the API's totals. */
export function RegistryPanel() {
  const total = useFetch(assetCountPath(''));
  const active = useFetch(assetCountPath('Active'));
  const maintenance = useFetch(assetCountPath('UnderMaintenance'));
  const retired = useFetch(assetCountPath('Retired'));
  const byStatus = { Active: countOf(active), UnderMaintenance: countOf(maintenance), Retired: countOf(retired) };
  const all = countOf(total);
  const ready = all !== null && Object.values(byStatus).every((value) => value !== null);

  return (
    <Panel eyebrow="Asset registry" count={all}>
      <div className={styles.registryTop}>
        <span className={styles.registryTotal}>{all ?? '—'}</span>
        <span className={styles.subtle}>machines on the estate</span>
      </div>
      <div className={styles.bar} aria-hidden="true">
        {ready && all > 0
          ? ASSET_STATUSES.map((status) => (
              <span
                key={status.value}
                className={clsx(styles.barSegment, styles[`dot-${status.tone}`])}
                style={{ flexGrow: byStatus[status.value] }}
              />
            ))
          : null}
      </div>
      <div className={styles.tiles} style={{ '--tiles': 3 }}>
        {ASSET_STATUSES.map((status) => (
          <CountTile
            key={status.value}
            label={status.label}
            tone={status.tone}
            count={byStatus[status.value]}
            to="/assets"
          />
        ))}
      </div>
    </Panel>
  );
}
