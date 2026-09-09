/** The loading state every page renders while a request is in flight. */
export function Spinner({ label = 'Loading…' }) {
  return (
    <div className="spinner" role="status" aria-live="polite">
      <span className="spinner__disc" aria-hidden="true" />
      <span className="spinner__label">{label}</span>
    </div>
  );
}

export default Spinner;
