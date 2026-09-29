import { RefreshCw } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { formatDateTime, syncTimetable } from '../services/workOrdersApi';
import styles from '../workorders.module.css';

/**
 * "Sync timetable now" — POST /api/timetable/sync, beside the slot finder, for a
 * FacilitiesManager (the one role the endpoint admits; the caller only renders it for them).
 *
 * The slot finder reads the CACHED campus timetable and never calls Google, so this is how a
 * manager refreshes that cache before looking for a time instead of waiting for the hourly
 * sync. The answer is ALWAYS a 200 — Google being down is not a failed request — and it is
 * shown as the API worded it: degraded or not, the failure reason by its name, how long Google
 * asked to wait when it said, the cache's age (null is "never synced", not 0), and the
 * staleness warning verbatim. Nothing here judges how stale is too stale.
 */
export function TimetableSyncButton({ onSynced }) {
  const [state, setState] = useState({ syncing: false, result: null, error: null });

  async function handleClick() {
    setState({ syncing: true, result: null, error: null });
    try {
      const result = await syncTimetable();
      setState({ syncing: false, result, error: null });
      onSynced?.(result);
    } catch (err) {
      // A 401/403 or no network at all — the sync's own failures come back as a 200.
      setState({ syncing: false, result: null, error: err });
    }
  }

  return (
    <div className={styles.timetableSync}>
      <div className={styles.timetableSyncBar}>
        <MxButton icon={RefreshCw} onClick={handleClick} disabled={state.syncing}>
          {state.syncing ? 'Syncing timetable…' : 'Sync timetable now'}
        </MxButton>
        <span className={styles.slotHint}>Offered times avoid the classes in the cached campus timetable.</span>
      </div>

      {state.result ? <SyncResult result={state.result} /> : null}
      {state.error ? (
        <p className={form.submitError} role="alert">
          {state.error.message}
        </p>
      ) : null}
    </div>
  );
}

function SyncResult({ result }) {
  return (
    <div className={result.degraded ? styles.timetableSyncDegraded : styles.timetableSyncOk} role="status">
      {result.degraded ? (
        <p>
          Google Calendar was not read — <span className="mx-mono">{result.failureReason}</span>. The cached timetable
          was kept as it was.
          {result.retryAfterSeconds !== null && result.retryAfterSeconds !== undefined
            ? ` Google asked to wait ${result.retryAfterSeconds} s before trying again.`
            : ''}
        </p>
      ) : (
        <p>
          Synced from Google Calendar: <span className="mx-mono">{result.syncedCount}</span> classes,{' '}
          <span className="mx-mono">{result.removedCount}</span> removed,{' '}
          <span className="mx-mono">{result.skippedCount}</span> skipped.
        </p>
      )}

      <p>
        {result.lastSyncedAt
          ? `Cache age: ${result.cacheAgeMinutes} min (last synced ${formatDateTime(result.lastSyncedAt)}).`
          : 'Nothing has ever been synced into the cache.'}
      </p>

      {/* Verbatim: the API's own sentence about what staleness costs. */}
      {result.stalenessWarning ? <p className={styles.timetableSyncWarning}>{result.stalenessWarning}</p> : null}
    </div>
  );
}

export default TimetableSyncButton;
