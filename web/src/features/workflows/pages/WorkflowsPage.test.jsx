import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ROLES } from '../../auth/services/roles';
import { renderWithAuth, signedInAs } from '../../../test/auth';
import { emptyPage, jsonResponse, stubFetch } from '../../../test/http';
import WorkflowsPage from './WorkflowsPage';

function renderPage() {
  return renderWithAuth(<WorkflowsPage />, signedInAs(ROLES.FacilitiesManager), { route: '/workflows' });
}

// Loading, error and empty states on a real page, fed through the real useFetch.
describe('WorkflowsPage states', () => {
  it('shows a skeleton while loading, not a blank screen', () => {
    stubFetch(() => new Promise(() => {})); // never answers

    const { container } = renderPage();

    expect(screen.getByRole('heading', { name: 'Workflows' })).toBeInTheDocument();
    expect(container.querySelector('[aria-busy="true"], [class*="skeleton" i]')).not.toBeNull();
  });

  it('shows the error state with the reason when the API fails', async () => {
    stubFetch(() => jsonResponse({}, 500));

    renderPage();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load workflows');
    expect(alert).toHaveTextContent('Request failed with status 500.');
  });

  it('shows the empty state — not an error — when there are no workflows', async () => {
    stubFetch(() => jsonResponse(emptyPage()));

    renderPage();

    expect(await screen.findByText('No workflows yet')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('lists the workflows the API sent', async () => {
    stubFetch(() =>
      jsonResponse({
        ...emptyPage(),
        totalCount: 1,
        totalPages: 1,
        items: [
          {
            id: 41,
            reportId: 12,
            objective: 'Projector in MAB101 keeps shutting off mid-lecture.',
            currentState: 'AwaitingManagerApproval',
            outcome: null,
            startedAt: '2026-09-30T08:00:00Z',
            completedAt: null,
            createdAt: '2026-09-30T08:00:00Z',
            updatedAt: '2026-09-30T08:05:00Z',
          },
        ],
      }),
    );

    renderPage();

    expect(await screen.findByText(/Projector in MAB101 keeps shutting off/)).toBeInTheDocument();
  });
});
