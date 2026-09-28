import { Search, SearchX, Workflow, X } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import PageHeader from '../../../components/ui/PageHeader';
import Pager from '../../../components/ui/Pager';
import { Panel } from '../../../components/ui/Panel';
import SelectMenu from '../../../components/ui/SelectMenu';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useDebounce from '../../../hooks/useDebounce';
import WorkflowRows from '../components/WorkflowRows';
import useWorkflows from '../hooks/useWorkflows';
import { DEFAULT_PAGE_SIZE, WORKFLOW_STATES, workflowStateLabel } from '../services/workflowsService';
import styles from '../workflows.module.css';

function RowsSkeleton() {
  return (
    <div className={styles.skeleton} role="status" aria-label="Loading workflows">
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className={styles.skeletonRow}>
          <Skeleton width={28} height={12} />
          <div style={{ flex: 1, display: 'grid', gap: 7 }}>
            <Skeleton width="58%" height={14} />
            <Skeleton width="30%" height={11} />
          </div>
          <Skeleton width={96} height={8} radius={999} />
          <Skeleton width={110} height={24} radius={999} />
        </div>
      ))}
    </div>
  );
}

export function WorkflowsPage() {
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
  // fetched — the hint under the box says so.
  const term = debouncedSearch.trim().toLowerCase();
  const visibleWorkflows = (data?.items ?? []).filter((workflow) =>
    term === '' ? true : workflow.objective.toLowerCase().includes(term),
  );

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Operations' }, { label: 'Workflows' }]}
        title="Workflows"
        lead="Agent runs, newest first. Each is created by the API and advanced in the background, so this is a snapshot of where they have got to."
        actions={
          data ? (
            <span className={styles.headerCount}>
              <strong>{data.totalCount}</strong> {data.totalCount === 1 ? 'workflow' : 'workflows'}
              {state ? ` ${workflowStateLabel(state).toLowerCase()}` : ''}
            </span>
          ) : null
        }
      />

      <div className={styles.controls}>
        <div className={styles.toolbar}>
          <label className={styles.search}>
            <span className="mx-visually-hidden">Filter this page by objective</span>
            <Search aria-hidden="true" />
            <input
              type="search"
              placeholder="Filter this page by objective"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              autoComplete="off"
            />
          </label>
          <SelectMenu
            ariaLabel="Filter by state"
            inlineLabel="State"
            emptyLabel="All"
            value={state}
            onChange={handleStateChange}
            options={WORKFLOW_STATES.map((value) => ({ value, label: workflowStateLabel(value) }))}
          />
          {search || state ? (
            <MxButton
              variant="ghost"
              icon={X}
              onClick={() => {
                setSearch('');
                handleStateChange('');
              }}
            >
              Clear
            </MxButton>
          ) : null}
        </div>
        <p className={styles.hint}>The search filters the workflows on this page only — the API has no search parameter yet.</p>
      </div>

      {isLoading ? <RowsSkeleton /> : null}

      {!isLoading && error ? (
        <Panel>
          <ErrorState title="Could not load workflows" message={error.message} />
        </Panel>
      ) : null}

      {!isLoading && !error && data ? (
        <>
          {visibleWorkflows.length === 0 ? (
            <Panel>
              <EmptyState
                icon={data.items.length === 0 ? Workflow : SearchX}
                title={
                  data.items.length === 0
                    ? state
                      ? `No workflows are ${workflowStateLabel(state).toLowerCase()}`
                      : 'No workflows yet'
                    : 'Nothing on this page matches'
                }
                body={
                  data.items.length === 0
                    ? 'A workflow starts when a report is filed. It appears here straight away and moves on as the agents run.'
                    : 'Try another word, or move to another page — the search only looks at the page already loaded.'
                }
              />
            </Panel>
          ) : (
            <WorkflowRows workflows={visibleWorkflows} />
          )}

          {data.totalCount > 0 ? (
            <Pager
              page={data.page}
              pageSize={data.pageSize}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={setPage}
              noun={data.totalCount === 1 ? 'workflow' : 'workflows'}
            />
          ) : null}
        </>
      ) : null}
    </section>
  );
}

export default WorkflowsPage;
