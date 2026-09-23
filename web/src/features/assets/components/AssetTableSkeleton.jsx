/**
 * The catalogue's loading state: placeholder rows in the table's own shape, so the page
 * does not jump when the data arrives. Still announced as loading to a screen reader.
 */
export function AssetTableSkeleton({ rows = 6 }) {
  return (
    <div className="asset-table asset-table--skeleton" role="status" aria-live="polite">
      <span className="visually-hidden">Loading assets…</span>
      {Array.from({ length: rows }, (_, index) => (
        // Placeholder rows have no identity; they are replaced wholesale when data arrives.
        // eslint-disable-next-line react/no-array-index-key
        <div key={index} className="skeleton-row" aria-hidden="true">
          <span className="skeleton skeleton--tag" />
          <span className="skeleton skeleton--name" />
          <span className="skeleton skeleton--short" />
          <span className="skeleton skeleton--short" />
          <span className="skeleton skeleton--pill" />
          <span className="skeleton skeleton--short" />
        </div>
      ))}
    </div>
  );
}

export default AssetTableSkeleton;
