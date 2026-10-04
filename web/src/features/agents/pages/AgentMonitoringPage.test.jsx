import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ROLES } from '../../auth/services/roles';
import { renderWithAuth, signedInAs } from '../../../test/auth';
import { jsonResponse, stubFetch } from '../../../test/http';
import AgentMonitoringPage from './AgentMonitoringPage';

const AGENTS = ['planner', 'clarifier', 'diagnostic', 'strategist', 'verification'];

function row(agentName, overrides = {}) {
  return {
    agentName,
    runs: 0,
    succeeded: 0,
    safeFailures: 0,
    callFailures: 0,
    rejected: 0,
    failed: 0,
    failureRate: 0,
    runsReportingAttempts: 0,
    retriedRuns: 0,
    retryRate: 0,
    timedRuns: 0,
    medianDurationMs: null,
    p95DurationMs: null,
    maxDurationMs: null,
    runsWithUsage: 0,
    promptTokens: null,
    completionTokens: null,
    totalTokens: null,
    averageTokensPerRun: null,
    estimatedCostUsd: null,
    ...overrides,
  };
}

function metrics({ configured = false, totals = {}, agents = {}, daily = [], slowest = [], costliest = [] } = {}) {
  return {
    fromDate: null,
    toDate: null,
    pricing: configured
      ? { configured: true, inputPricePerMillionTokensUsd: 0.3, outputPricePerMillionTokensUsd: 2.5 }
      : { configured: false, inputPricePerMillionTokensUsd: null, outputPricePerMillionTokensUsd: null },
    totals: row(null, totals),
    agents: AGENTS.map((name) => row(name, agents[name])),
    daily,
    slowestRuns: slowest,
    costliestRuns: costliest,
  };
}

const RUN = {
  stepId: 7,
  workflowId: 41,
  agentName: 'diagnostic',
  validationResult: 'Ok',
  durationMs: 9700,
  attempts: 2,
  promptTokens: 1830,
  completionTokens: 240,
  estimatedCostUsd: 0.001149,
  createdAt: '2026-09-30T08:00:00Z',
};

function renderPage() {
  return renderWithAuth(<AgentMonitoringPage />, signedInAs(ROLES.FacilitiesManager), { route: '/agent-monitoring' });
}

describe('AgentMonitoringPage', () => {
  it('asks the API for its figures, and shows a skeleton while it waits', () => {
    const fetch = stubFetch(() => new Promise(() => {}));

    const { container } = renderPage();

    expect(screen.getByRole('heading', { name: 'Agent monitoring' })).toBeInTheDocument();
    expect(container.querySelector('[aria-busy="true"], [class*="skeleton" i]')).not.toBeNull();
    expect(fetch.mock.calls[0][0]).toMatch(/\/api\/analytics\/agents$/);
  });

  it('shows the error with the reason when the API fails', async () => {
    stubFetch(() => jsonResponse({}, 500));

    renderPage();

    const alerts = await screen.findAllByRole('alert');
    expect(alerts[0]).toHaveTextContent('Request failed with status 500.');
  });

  it('says there is not enough data yet — not an error, not zeros — when no agent has run', async () => {
    stubFetch(() => jsonResponse(metrics()));

    renderPage();

    expect((await screen.findAllByText('Not enough data yet')).length).toBeGreaterThan(0);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows each agent as the API counted it, and says when no price is configured', async () => {
    stubFetch(() =>
      jsonResponse(
        metrics({
          totals: {
            runs: 7,
            succeeded: 4,
            failed: 3,
            failureRate: 42.86,
            timedRuns: 5,
            medianDurationMs: 500,
            p95DurationMs: 1200,
            runsWithUsage: 5,
            promptTokens: 4300,
            completionTokens: 450,
            totalTokens: 4750,
          },
          agents: {
            diagnostic: { runs: 2, failed: 2, failureRate: 100, timedRuns: 1, medianDurationMs: 900 },
            clarifier: { runs: 1, succeeded: 1, runsReportingAttempts: 1, retriedRuns: 1, retryRate: 100, runsWithUsage: 1, averageTokensPerRun: 640 },
          },
          slowest: [RUN],
          costliest: [RUN],
        }),
      ),
    );

    renderPage();

    const table = await screen.findByRole('table');
    const diagnostic = within(table).getByRole('row', { name: /Diagnostic/ });
    expect(diagnostic).toHaveTextContent('100%');
    expect(diagnostic).toHaveTextContent('2 of 2 runs');
    expect(diagnostic).toHaveTextContent('900 ms');
    // Nothing reported for it: a dash, never a 0.
    expect(diagnostic).toHaveTextContent('—');

    expect(screen.getByText('42.86%')).toBeInTheDocument();
    expect(screen.getByText('4,750')).toBeInTheDocument();
    expect(screen.getAllByText(/Price not configured/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/\$0\.00/)).not.toBeInTheDocument();

    // Each run links to its workflow, where the full trace is.
    const links = screen.getAllByRole('link', { name: /Diagnostic/ });
    expect(links[0]).toHaveAttribute('href', '/workflows/41');
  });

  it('shows the API’s estimated cost once a price is configured', async () => {
    stubFetch(() =>
      jsonResponse(
        metrics({
          configured: true,
          totals: { runs: 1, succeeded: 1, runsWithUsage: 1, promptTokens: 1830, completionTokens: 240, totalTokens: 2070, estimatedCostUsd: 0.001149 },
          costliest: [RUN],
        }),
      ),
    );

    renderPage();

    expect((await screen.findAllByText(/\$0\.001149/)).length).toBeGreaterThan(0);
    expect(screen.getByText(/\$0\.30 in \/ \$2\.50 out per million tokens/)).toBeInTheDocument();
  });
});
