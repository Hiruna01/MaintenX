import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';

/**
 * One dashboard panel and its four states — loading, error, empty and data. Every chart on
 * the metrics page goes through this, so none of them can render an empty area: an empty
 * chart looks like a broken one, and "not enough data yet" is a different fact from both a
 * failure and a zero.
 *
 * The panel keeps a minimum height in every state, so the page does not jump as the
 * request settles.
 */
export function MetricsPanel({ title, lead, isLoading, error, isEmpty, emptyBody, children }) {
  return (
    <section className="metrics-panel" aria-label={title}>
      <header className="metrics-panel__head">
        <h2>{title}</h2>
        {lead ? <p className="metrics-panel__lead">{lead}</p> : null}
      </header>

      <div className="metrics-panel__body">
        {isLoading ? <Spinner label={`Loading ${title.toLowerCase()}…`} /> : null}

        {!isLoading && error ? <ErrorMessage title={`Could not load ${title.toLowerCase()}`} message={error.message} /> : null}

        {!isLoading && !error && isEmpty ? (
          <div className="empty-state metrics-panel__empty">
            <p className="empty-state__title">Not enough data yet</p>
            <p className="empty-state__body">{emptyBody}</p>
          </div>
        ) : null}

        {!isLoading && !error && !isEmpty ? children : null}
      </div>
    </section>
  );
}

export default MetricsPanel;
