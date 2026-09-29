import { ArrowDown, ChevronRight } from 'lucide-react';
import { Link } from 'react-router-dom';

import { StatusPill } from '../../../components/ui/Pill';
import { ASSET_SORTS, formatDateOnly } from '../services/assetsApi';
import styles from '../assets.module.css';
import TagChip from './TagChip';

function isModifiedClick(event) {
  return event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0;
}

function SortHeader({ label, sortKey, sort, onSortChange }) {
  const isActive = sort === sortKey;
  return (
    <th scope="col" aria-sort={isActive ? 'ascending' : 'none'}>
      <button type="button" className={styles.sortButton} data-active={isActive} onClick={() => onSortChange(sortKey)}>
        {label}
        <ArrowDown aria-hidden="true" />
      </button>
    </th>
  );
}

/**
 * The catalogue as floating rows. Presentational — it is handed a page of assets and the
 * lookups, and fetches nothing. A plain click opens the quick look; Cmd/Ctrl-click on the
 * name still opens the full page in a new tab, because it is a real link.
 */
export function AssetRows({ assets, categoriesById, roomsById, sort, onSortChange, onPreview }) {
  return (
    <div className={styles.rowsWrap}>
      <table className={styles.rows}>
        <caption className="mx-visually-hidden">Asset catalogue</caption>
        <thead>
          <tr>
            <th scope="col">Tag</th>
            <SortHeader label="Asset" sortKey={ASSET_SORTS.Name} sort={sort} onSortChange={onSortChange} />
            <th scope="col">Category</th>
            <th scope="col">Location</th>
            <th scope="col">Status</th>
            <SortHeader label="Installed" sortKey={ASSET_SORTS.InstalledOn} sort={sort} onSortChange={onSortChange} />
            <th scope="col">
              <span className="mx-visually-hidden">Open</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {assets.map((asset, index) => {
            const room = roomsById.get(asset.roomId);
            return (
              <tr
                key={asset.id}
                className={styles.row}
                style={{ animationDelay: `${Math.min(index, 10) * 28}ms` }}
                onClick={(event) => {
                  if (event.target.closest('a') || isModifiedClick(event)) return;
                  onPreview(asset.id);
                }}
              >
                <td data-label="Tag">
                  <TagChip tag={asset.assetTag} />
                </td>
                <td className={styles.rowMain}>
                  <Link
                    className={styles.rowName}
                    to={`/assets/${asset.id}`}
                    onClick={(event) => {
                      if (isModifiedClick(event)) return;
                      event.preventDefault();
                      onPreview(asset.id);
                    }}
                  >
                    {asset.name}
                  </Link>
                  <span className={styles.rowSub}>
                    {[asset.manufacturer, asset.model].filter(Boolean).join(' · ') || 'Make and model not recorded'}
                  </span>
                </td>
                <td data-label="Category">
                  {categoriesById.get(asset.assetCategoryId)?.name ?? `#${asset.assetCategoryId}`}
                </td>
                <td data-label="Location">
                  {room ? (
                    <span className={styles.rowRoom}>
                      <span className="mx-mono">{room.code}</span>
                      <span>{room.name}</span>
                    </span>
                  ) : (
                    `#${asset.roomId}`
                  )}
                </td>
                <td data-label="Status">
                  <StatusPill status={asset.status} />
                </td>
                <td data-label="Installed" className={styles.rowDate}>
                  {formatDateOnly(asset.installedOn)}
                </td>
                <td className={styles.rowChevron} aria-hidden="true">
                  <ChevronRight />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export default AssetRows;
