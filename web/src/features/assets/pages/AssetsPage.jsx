import { Boxes, Plus, SearchX } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Outlet } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import PageHeader from '../../../components/ui/PageHeader';
import Pager from '../../../components/ui/Pager';
import { Panel } from '../../../components/ui/Panel';
import Segmented from '../../../components/ui/Segmented';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useDebounce from '../../../hooks/useDebounce';
import useAuth from '../../auth/hooks/useAuth';
import { ADMIN_ROLES, hasRole } from '../../auth/services/roles';
import AssetQuickLook from '../components/AssetQuickLook';
import AssetRows from '../components/AssetRows';
import AssetToolbar from '../components/AssetToolbar';
import useAssetLookups from '../hooks/useAssetLookups';
import useAssets from '../hooks/useAssets';
import useAssetStatusCounts from '../hooks/useAssetStatusCounts';
import { ASSET_SORTS, DEFAULT_PAGE_SIZE } from '../services/assetsApi';
import styles from '../assets.module.css';

const EMPTY_FILTERS = { search: '', categoryId: '', roomId: '', status: '' };

const STATUS_TABS = [
  { value: '', label: 'All' },
  { value: 'Active', label: 'Active' },
  { value: 'UnderMaintenance', label: 'Under maintenance' },
  { value: 'Retired', label: 'Retired' },
];

function RowsSkeleton() {
  return (
    <div className={styles.rowsSkeleton} role="status" aria-label="Loading assets">
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className={styles.rowSkeleton}>
          <Skeleton width={128} height={24} radius={999} />
          <div style={{ flex: 1, display: 'grid', gap: 6 }}>
            <Skeleton width="42%" height={14} />
            <Skeleton width="24%" height={11} />
          </div>
          <Skeleton width={84} height={24} radius={999} />
          <Skeleton width={80} height={12} />
        </div>
      ))}
    </div>
  );
}

/**
 * The asset catalogue. Reading it is open to every signed-in role; only an Admin is offered
 * "Register asset", which opens the register panel over this list (the nested /assets/new
 * route renders into the Outlet below).
 */
export function AssetsPage() {
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sort, setSort] = useState(ASSET_SORTS.Name);
  const [page, setPage] = useState(1);
  // The id outlives `open` so the quick look can play its exit animation with content in it.
  const [preview, setPreview] = useState({ id: null, open: false });

  const { role } = useAuth();
  const isAdmin = hasRole(role, ADMIN_ROLES);

  // One request after typing stops, instead of one per keystroke.
  const debouncedSearch = useDebounce(filters.search, 400);

  const { data, isLoading, error } = useAssets({
    ...filters,
    search: debouncedSearch,
    sort,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });
  const counts = useAssetStatusCounts({ ...filters, search: debouncedSearch });
  const lookups = useAssetLookups();

  const categoriesById = useMemo(
    () => new Map(lookups.categories.map((category) => [category.id, category])),
    [lookups.categories],
  );
  const roomsById = useMemo(() => new Map(lookups.rooms.map((room) => [room.id, room])), [lookups.rooms]);

  function handleFilterChange(name, value) {
    setFilters((current) => ({ ...current, [name]: value }));
    // A new filter means a new result set, so page 3 of the old one is meaningless.
    setPage(1);
  }

  function handleClear() {
    setFilters(EMPTY_FILTERS);
    setPage(1);
  }

  function handleSortChange(nextSort) {
    setSort(nextSort);
    setPage(1);
  }

  const hasFilters = Object.values(filters).some(Boolean);

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Estate' }, { label: 'Assets' }]}
        title="Assets"
        lead="Every machine on the estate — where it is, what state it is in, and the sticker it carries."
        actions={
          isAdmin ? (
            <MxButton variant="primary" icon={Plus} to="/assets/new">
              Register asset
            </MxButton>
          ) : null
        }
      />

      <div className={styles.listControls}>
        <Segmented
          label="Filter by status"
          value={filters.status}
          onChange={(value) => handleFilterChange('status', value)}
          options={STATUS_TABS.map((tab) => ({ ...tab, count: counts[tab.value] }))}
        />
        <AssetToolbar
          values={filters}
          onChange={handleFilterChange}
          onClear={handleClear}
          categories={lookups.categories}
          rooms={lookups.rooms}
          lookupsLoading={lookups.isLoading}
          sort={sort}
          onSortChange={handleSortChange}
        />
      </div>

      {/* The pickers and the name columns degrade without the lookups; say so rather than
          silently showing "#3" in the Category column. */}
      {!lookups.isLoading && lookups.error ? (
        <p className={styles.inlineWarning} role="alert">
          Categories and rooms could not be loaded — those filters are unavailable and names show as ids.
        </p>
      ) : null}

      {isLoading ? <RowsSkeleton /> : null}

      {!isLoading && error ? (
        <Panel>
          <ErrorState title="Could not load assets" message={error.message} />
        </Panel>
      ) : null}

      {!isLoading && !error && data ? (
        data.items.length === 0 ? (
          <Panel>
            <EmptyState
              icon={hasFilters ? SearchX : Boxes}
              title={hasFilters ? 'No assets match these filters' : 'No assets registered yet'}
              body={
                hasFilters
                  ? 'Try a different search, or clear the filters to see the whole estate.'
                  : isAdmin
                    ? 'Register the first piece of equipment to start its service history.'
                    : 'An Admin registers equipment here as it arrives on the estate.'
              }
              action={
                hasFilters ? (
                  <MxButton onClick={handleClear}>Clear filters</MxButton>
                ) : isAdmin ? (
                  <MxButton variant="primary" icon={Plus} to="/assets/new">
                    Register asset
                  </MxButton>
                ) : null
              }
            />
          </Panel>
        ) : (
          <>
            <AssetRows
              assets={data.items}
              categoriesById={categoriesById}
              roomsById={roomsById}
              sort={sort}
              onSortChange={handleSortChange}
              onPreview={(id) => setPreview({ id, open: true })}
            />
            <Pager
              page={data.page}
              pageSize={data.pageSize}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={setPage}
              noun={data.totalCount === 1 ? 'asset' : 'assets'}
            />
          </>
        )
      ) : null}

      {preview.id !== null ? (
        <AssetQuickLook
          key={preview.id}
          assetId={preview.id}
          open={preview.open}
          canEdit={isAdmin}
          onOpenChange={(open) => setPreview((current) => ({ ...current, open }))}
        />
      ) : null}

      {/* /assets/new renders its slide-over here, over the list. */}
      <Outlet />
    </section>
  );
}

export default AssetsPage;
