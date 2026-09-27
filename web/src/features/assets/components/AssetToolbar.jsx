import { Search, X } from 'lucide-react';

import MxButton from '../../../components/ui/Button';
import SelectMenu from '../../../components/ui/SelectMenu';
import { ASSET_SORTS } from '../services/assetsApi';
import styles from '../assets.module.css';

const SORT_OPTIONS = [
  { value: ASSET_SORTS.Name, label: 'Name' },
  { value: ASSET_SORTS.InstalledOn, label: 'Installation date' },
];

/**
 * Search, the two exact pickers and the sort. The search is server-side and matches name OR
 * tag in one box — a tag is what someone holding the equipment has. Only the two orders the
 * API offers are listed, both ascending.
 */
export function AssetToolbar({ values, onChange, onClear, categories, rooms, lookupsLoading, sort, onSortChange }) {
  const hasFilters = Boolean(values.search || values.categoryId || values.roomId);

  return (
    <div className={styles.toolbar}>
      <label className={styles.search}>
        <span className="mx-visually-hidden">Search assets</span>
        <Search aria-hidden="true" />
        <input
          type="search"
          placeholder="Search by name or tag"
          value={values.search}
          onChange={(event) => onChange('search', event.target.value)}
          autoComplete="off"
        />
      </label>

      <div className={styles.toolbarPickers}>
        <SelectMenu
          ariaLabel="Filter by category"
          inlineLabel="Category"
          emptyLabel="All"
          value={values.categoryId}
          onChange={(next) => onChange('categoryId', next)}
          options={categories.map((category) => ({ value: category.id, label: category.name }))}
          disabled={lookupsLoading}
        />
        <SelectMenu
          ariaLabel="Filter by room"
          inlineLabel="Room"
          emptyLabel="All"
          value={values.roomId}
          onChange={(next) => onChange('roomId', next)}
          options={rooms.map((room) => ({ value: room.id, label: room.code ? `${room.code} · ${room.name}` : room.name }))}
          disabled={lookupsLoading}
        />
        <SelectMenu
          ariaLabel="Sort assets"
          inlineLabel="Sort"
          value={sort}
          onChange={onSortChange}
          options={SORT_OPTIONS}
        />
        {hasFilters ? (
          <MxButton variant="ghost" icon={X} onClick={onClear}>
            Clear
          </MxButton>
        ) : null}
      </div>
    </div>
  );
}

export default AssetToolbar;
