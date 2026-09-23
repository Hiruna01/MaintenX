import { ASSET_STATUSES, enumLabel, roomLabel } from '../services/assetsApi';

/**
 * The search box and the three exact filters. Every value is controlled by the page; this
 * component only renders them and reports changes upward.
 *
 * The search is server-side — GET /api/assets matches name OR tag in one box, because a
 * tag is what someone holding the equipment has — so the hint says what it searches
 * rather than implying more.
 */
export function AssetFilters({ values, onChange, onClear, categories, rooms, lookupsLoading }) {
  const hasFilters = Boolean(
    values.search || values.categoryId || values.roomId || values.status,
  );

  return (
    <div className="asset-filters">
      <div className="asset-filters__search">
        <label htmlFor="asset-search" className="visually-hidden">
          Search assets
        </label>
        <svg className="asset-filters__search-icon" viewBox="0 0 20 20" aria-hidden="true">
          <circle cx="8.5" cy="8.5" r="5.5" fill="none" stroke="currentColor" strokeWidth="1.6" />
          <path d="m13 13 4 4" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
        </svg>
        <input
          id="asset-search"
          type="search"
          placeholder="Search by name or asset tag…"
          value={values.search}
          onChange={(event) => onChange('search', event.target.value)}
          autoComplete="off"
        />
      </div>

      <div className="asset-filters__selects">
        <label className="asset-filters__select">
          <span>Category</span>
          <select
            value={values.categoryId}
            onChange={(event) => onChange('categoryId', event.target.value)}
            disabled={lookupsLoading}
          >
            <option value="">All categories</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </label>

        <label className="asset-filters__select">
          <span>Room</span>
          <select
            value={values.roomId}
            onChange={(event) => onChange('roomId', event.target.value)}
            disabled={lookupsLoading}
          >
            <option value="">All rooms</option>
            {rooms.map((room) => (
              <option key={room.id} value={room.id}>
                {roomLabel(room)}
              </option>
            ))}
          </select>
        </label>

        <label className="asset-filters__select">
          <span>Status</span>
          <select value={values.status} onChange={(event) => onChange('status', event.target.value)}>
            <option value="">Any status</option>
            {ASSET_STATUSES.map((status) => (
              <option key={status} value={status}>
                {enumLabel(status)}
              </option>
            ))}
          </select>
        </label>

        <button
          type="button"
          className="asset-filters__clear"
          onClick={onClear}
          disabled={!hasFilters}
        >
          Clear
        </button>
      </div>
    </div>
  );
}

export default AssetFilters;
