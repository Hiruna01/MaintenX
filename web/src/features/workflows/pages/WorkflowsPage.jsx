import { useState } from 'react';

import ErrorMessage from '../../../components/ErrorMessage';
import Pagination from '../../../components/Pagination';
import Spinner from '../../../components/Spinner';
import useDebounce from '../../../hooks/useDebounce';
import WorkflowFilters from '../components/WorkflowFilters';
import WorkflowTable from '../components/WorkflowTable';
import useWorkflows from '../hooks/useWorkflows';
import { DEFAULT_PAGE_SIZE } from '../services/workflowsService';

export function WorkflowsPage() {
  // Local UI state stays in useState; only auth/session is app-wide Context.
  const [search, setSearch] = useState('');
  const [state, setState] = useState('');
  const [page, setPage] = useState(1);

  // One update after typing stops, instead of one per keystroke.
  const debouncedSearch = useDebounce(search, 400);

  const { data, isLoading, error } = useWorkflows({ page, pageSize: DEFAULT_PAGE_SIZE, state });

  function handleStateChange(nextState) {
    setState(nextState);
    // A new filter means a new result set, so page 2 of the old one is meaningless.
    setPage(1);
  }

  // Search is applied here rather than in the query string because GET /api/workflows
  // takes only `state`, `page` and `pageSize`. It therefore narrows the page already
  // fetched — see the hint under the search box.
  const term = debouncedSearch.trim().toLowerCase();
  const visibleWorkflows = (data?.items ?? []).filter((workflow) =>
    term === '' ? true : workflow.objective.toLowerCase().includes(term),
  );

  return (
    <section className="page">
      <h1>Workflows</h1>
      <p className="page__lead">
        Agent workflows, newest first. Each one is created by the API and advanced in the
        background, so this list is a snapshot of where they have got to.
      </p>

      <WorkflowFilters
        search={search}
        onSearchChange={setSearch}
        state={state}
        onStateChange={handleStateChange}
      />

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading workflows…" /> : null}

      {!isLoading && error ? (
        <ErrorMessage title="Could not load workflows" message={error.message} />
      ) : null}

      {!isLoading && !error && data ? (
        <>
          {visibleWorkflows.length === 0 ? (
            <p className="empty">
              {data.items.length === 0
                ? 'No workflows have been started yet.'
                : 'No workflows on this page match your search.'}
            </p>
          ) : (
            <WorkflowTable workflows={visibleWorkflows} />
          )}

          <Pagination
            page={data.page}
            totalPages={data.totalPages}
            totalCount={data.totalCount}
            onPageChange={setPage}
          />
        </>
      ) : null}
    </section>
  );
}

export default WorkflowsPage;
