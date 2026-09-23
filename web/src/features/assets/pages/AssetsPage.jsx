import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Pagination from '../../../components/Pagination';
import useDebounce from '../../../hooks/useDebounce';
import useAuth from '../../auth/hooks/useAuth';
import { ADMIN_ROLES, hasRole } from '../../auth/services/roles';
import AssetFilters from '../components/AssetFilters';
import AssetTable from '../components/AssetTable';
import AssetTableSkeleton from '../components/AssetTableSkeleton';
import useAssetLookups from '../hooks/useAssetLookups';
import useAssets from '../hooks/useAssets';
import { ASSET_SORTS, DEFAULT_PAGE_SIZE } from '../services/assetsApi';

const EMPTY_FILTERS = { search: '', categoryId: '', roomId: '', status: '' };

/**
 * The asset catalogue. Reading it is open to every signed-in role — a technician looks a
 * machine up before a visit, a reporter checks what is in a room — but only an Admin is
 * offered "Register asset". The route behind that button refuses anyone else anyway;
 * showing a link that leads to "not authorised" would be a bad interface.
 */
export function AssetsPage() {
  // Local UI state stays in useState; only auth/session is app-wide Context.
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sort, setSort] = useState(ASSET_SORTS.Name);
  const [page, setPage] = useState(1);

  const { role } = useAuth();
  const isAdmin = hasRole(role, ADMIN_ROLES);

  // One request after typing stops, instead of one per keystroke. The search is a real
  // server-side parameter here, unlike the workflows list.
  const debouncedSearch = useDebounce(filters.search, 400);

  const { data, isLoading, error } = useAssets({
    ...filters,
    search: debouncedSearch,
    sort,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });
  const lookups = useAssetLookups();

  const categoriesById = useMemo(
    () => new Map(lookups.categories.map((category) => [category.id, category])),
    [lookups.categories],
  );
  const roomsById = useMemo(
    () => new Map(lookups.rooms.map((room) => [room.id, room])),
    [lookups.rooms],
  );

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
    <section className="page assets-page">
      <header className="page-header">
        <div>
          <p className="page-header__eyebrow">Asset registry</p>
          <h1>Assets</h1>
          <p className="page__lead">
            Every piece of equipment on the estate, where it is, and what state it is in.
          </p>
        </div>

        {isAdmin ? (
          <Link className="button button--primary page-header__action" to="/assets/new">
            <span aria-hidden="true">+</span> Register asset
          </Link>
        ) : null}
      </header>

      <AssetFilters
        values={filters}
        onChange={handleFilterChange}
        onClear={handleClear}
        categories={lookups.categories}
        rooms={lookups.rooms}
        lookupsLoading={lookups.isLoading}
      />

      {/* The pickers and the name columns degrade without the lookups; say so rather than
          silently showing "#3" in the Category column. */}
      {!lookups.isLoading && lookups.error ? (
        <ErrorMessage
          title="Categories and rooms could not be loaded"
          message="Filters by category and room are unavailable, and names show as ids."
        />
      ) : null}

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <AssetTableSkeleton /> : null}

      {!isLoading && error ? (
        <ErrorMessage title="Could not load assets" message={error.message} />
      ) : null}

      {!isLoading && !error && data ? (
        <>
          {data.items.length === 0 ? (
            <div className="empty-state">
              <p className="empty-state__title">
                {hasFilters ? 'No assets match these filters' : 'No assets registered yet'}
              </p>
              <p className="empty-state__body">
                {hasFilters
                  ? 'Try a different search, or clear the filters to see the whole estate.'
                  : isAdmin
                    ? 'Register the first piece of equipment to start its service history.'
                    : 'An Admin registers equipment here as it arrives on the estate.'}
              </p>
            </div>
          ) : (
            <AssetTable
              assets={data.items}
              categoriesById={categoriesById}
              roomsById={roomsById}
              sort={sort}
              onSortChange={handleSortChange}
            />
          )}

          {data.totalCount > 0 ? (
            <Pagination
              page={data.page}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={setPage}
            />
          ) : null}
        </>
      ) : null}
    </section>
  );
}

export default AssetsPage;
