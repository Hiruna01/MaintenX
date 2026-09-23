import { Link } from 'react-router-dom';

import { ASSET_SORTS, formatDateOnly, roomLabel } from '../services/assetsApi';
import AssetStatusBadge from './AssetStatusBadge';

/**
 * A column header that sorts. Only Name and Installed are sortable because those are the
 * two orders GET /api/assets offers; a clickable header that sorted only the page already
 * fetched would put a different order on every page and look like a server sort.
 * Both orders are ascending — the API takes no direction — so there is nothing to toggle.
 */
function SortableHeader({ label, sortKey, currentSort, onSortChange }) {
  const isActive = currentSort === sortKey;

  return (
    <th scope="col" aria-sort={isActive ? 'ascending' : 'none'}>
      <button
        type="button"
        className={`sort-button ${isActive ? 'sort-button--active' : ''}`.trim()}
        onClick={() => onSortChange(sortKey)}
      >
        {label}
        <svg className="sort-button__icon" viewBox="0 0 12 12" aria-hidden="true">
          <path d="M6 2.5v7M3 6.5l3 3 3-3" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>
    </th>
  );
}

/**
 * Presentational only — it receives the page of assets and the lookups and fetches nothing.
 * The list DTO carries ids, not names, so category and room names come from the lookups.
 */
export function AssetTable({ assets, categoriesById, roomsById, sort, onSortChange }) {
  return (
    <div className="asset-table">
      <table>
        <caption className="visually-hidden">Asset catalogue</caption>
        <thead>
          <tr>
            <th scope="col">Asset tag</th>
            <SortableHeader label="Name" sortKey={ASSET_SORTS.Name} currentSort={sort} onSortChange={onSortChange} />
            <th scope="col">Category</th>
            <th scope="col">Room</th>
            <th scope="col">Status</th>
            <SortableHeader label="Installed" sortKey={ASSET_SORTS.InstalledOn} currentSort={sort} onSortChange={onSortChange} />
          </tr>
        </thead>
        <tbody>
          {assets.map((asset) => (
            <tr key={asset.id}>
              <td>
                <span className="asset-tag">{asset.assetTag}</span>
              </td>
              <td>
                <Link className="asset-table__name" to={`/assets/${asset.id}`}>
                  {asset.name}
                </Link>
                {asset.manufacturer || asset.model ? (
                  <span className="asset-table__sub">
                    {[asset.manufacturer, asset.model].filter(Boolean).join(' ')}
                  </span>
                ) : null}
              </td>
              <td>{categoriesById.get(asset.assetCategoryId)?.name ?? `#${asset.assetCategoryId}`}</td>
              <td>{roomsById.has(asset.roomId) ? roomLabel(roomsById.get(asset.roomId)) : `#${asset.roomId}`}</td>
              <td>
                <AssetStatusBadge status={asset.status} />
              </td>
              <td className="asset-table__date">{formatDateOnly(asset.installedOn)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default AssetTable;
