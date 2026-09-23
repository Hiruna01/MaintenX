import { formatDateOnly } from '../services/assetsApi';
import OutcomeBadge from './OutcomeBadge';

/**
 * The asset's service history as a timeline, OLDEST FIRST — the order the API returns it
 * and the order it is rendered in. A repeat failure only reads as one when the visits are
 * in the order they happened, so nothing here re-sorts.
 *
 * Every technician note is shown VERBATIM: no truncation, no "read more", no tidying.
 * The notes are terse, abbreviated and sometimes vague, and reading them raw is the point —
 * the fault this history is evidence of is spread across several of them, and a note cut
 * to its first line can drop exactly the clause that matters. `white-space: pre-wrap`
 * keeps any line breaks the technician typed.
 */
export function ServiceTimeline({ records }) {
  return (
    <ol className="timeline">
      {records.map((record, index) => (
        <li key={record.id} className={`timeline__item timeline__item--${record.outcome}`}>
          <span className="timeline__marker" aria-hidden="true" />

          <article className="timeline__card">
            <header className="timeline__head">
              <time className="timeline__date" dateTime={record.servicedOn}>
                {formatDateOnly(record.servicedOn, {
                  weekday: 'short',
                  day: 'numeric',
                  month: 'long',
                  year: 'numeric',
                })}
              </time>
              <OutcomeBadge outcome={record.outcome} />
              <span className="timeline__count">
                Visit {index + 1} of {records.length}
              </span>
            </header>

            {record.technicianNote ? (
              <blockquote className="timeline__note">{record.technicianNote}</blockquote>
            ) : (
              <p className="timeline__note timeline__note--empty">No note recorded.</p>
            )}

            <footer className="timeline__meta">
              <span>{record.technicianName}</span>
              {record.workOrderId ? <span>Work order #{record.workOrderId}</span> : null}
            </footer>
          </article>
        </li>
      ))}
    </ol>
  );
}

export default ServiceTimeline;
