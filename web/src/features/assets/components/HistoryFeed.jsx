import clsx from 'clsx';
import { CircleCheck, CircleDashed, Replace, Timer } from 'lucide-react';

import { StatusPill } from '../../../components/ui/Pill';
import { formatDateOnly } from '../services/assetsApi';
import styles from '../assets.module.css';

const OUTCOME_ICONS = {
  Resolved: CircleCheck,
  TemporaryFix: Timer,
  PartReplaced: Replace,
  NoFaultFound: CircleDashed,
};

/**
 * The service history as an activity feed, OLDEST FIRST — the order the API sends it. A
 * repeat failure only reads as one in the order it happened, so nothing here re-sorts.
 *
 * Every technician note is shown VERBATIM, whole, with its line breaks: the fault is spread
 * across several terse notes, and a note cut short can drop exactly the clause that matters.
 *
 * `startIndex` / `total` keep "Visit n of m" true when only part of the history is shown.
 */
export function HistoryFeed({ records, startIndex = 0, total }) {
  const count = total ?? records.length;
  return (
    <ol className={styles.feed}>
      {records.map((record, index) => {
        const Icon = OUTCOME_ICONS[record.outcome] ?? CircleDashed;
        return (
          <li
            key={record.id}
            className={clsx(styles.feedItem, styles[`feed-${record.outcome}`])}
            style={{ animationDelay: `${Math.min(index, 8) * 50}ms` }}
          >
            <span className={styles.feedMarker} aria-hidden="true">
              <Icon strokeWidth={1.9} />
            </span>

            <div className={styles.feedBody}>
              <header className={styles.feedHead}>
                <span className={styles.feedWho}>{record.technicianName}</span>
                <StatusPill status={record.outcome} />
                <time className={styles.feedDate} dateTime={record.servicedOn}>
                  {formatDateOnly(record.servicedOn, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })}
                </time>
              </header>

              {record.technicianNote ? (
                <blockquote className={styles.feedNote}>{record.technicianNote}</blockquote>
              ) : (
                <p className={clsx(styles.feedNote, styles.feedNoteEmpty)}>No note recorded.</p>
              )}

              <footer className={styles.feedMeta}>
                <span>
                  Visit {startIndex + index + 1} of {count}
                </span>
                {record.workOrderId ? <span>Work order #{record.workOrderId}</span> : <span>No linked work order</span>}
              </footer>
            </div>
          </li>
        );
      })}
    </ol>
  );
}

export default HistoryFeed;
