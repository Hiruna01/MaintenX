import { describe, expect, it } from 'vitest';

import { tokensLabel } from '../../reports/services/agentSteps';
import {
  buildAgentMetricsPath,
  dailyChartRows,
  describeCost,
  formatDurationMs,
  formatTokens,
  formatUsd,
} from './agentMetricsApi';

describe('agentMetricsApi', () => {
  it('sends only the range it was given, as the date input wrote it', () => {
    expect(buildAgentMetricsPath()).toBe('/api/analytics/agents');
    expect(buildAgentMetricsPath({ fromDate: '2026-09-01', toDate: '' })).toBe('/api/analytics/agents?fromDate=2026-09-01');
    expect(buildAgentMetricsPath({ fromDate: '2026-09-01', toDate: '2026-09-30' })).toBe(
      '/api/analytics/agents?fromDate=2026-09-01&toDate=2026-09-30',
    );
  });

  it('shows nothing-to-compute-from as a dash, never as zero', () => {
    expect(formatTokens(null)).toBe('—');
    expect(formatDurationMs(null)).toBe('—');
    expect(formatUsd(null)).toBe('—');
    expect(formatTokens(0)).toBe('0');
  });

  it('formats durations in ms below a second and in seconds above', () => {
    expect(formatDurationMs(850)).toBe('850 ms');
    expect(formatDurationMs(9700)).toBe('9.7 s');
  });

  it('keeps the six places a single run’s cost needs', () => {
    expect(formatUsd(0.002415)).toBe('$0.002415');
    expect(formatUsd(0)).toBe('$0.00');
  });

  it('tells no price, no usage and a real estimate apart', () => {
    expect(describeCost(null, { configured: false }, 5)).toBe('Price not configured');
    expect(describeCost(null, { configured: true }, 0)).toBe('No usage reported');
    expect(describeCost(0.00028, { configured: true }, 1)).toBe('$0.00028');
  });

  it('leaves a day whose runs reported nothing without a bar, and keeps a day with no runs at 0', () => {
    const rows = dailyChartRows([
      { date: '2026-09-29', runs: 0, runsWithUsage: 0, promptTokens: 0, completionTokens: 0, estimatedCostUsd: null },
      { date: '2026-09-30', runs: 3, runsWithUsage: 0, promptTokens: null, completionTokens: null, estimatedCostUsd: null },
    ]);

    expect(rows[0].promptTokens).toBe(0);
    expect(rows[1].promptTokens).toBeNull();
    expect(rows[1].runs).toBe(3);
  });
});

describe('tokensLabel', () => {
  it('shows the reported counts on an agent run, and nothing when none were reported', () => {
    expect(tokensLabel({ promptTokens: 1180, completionTokens: 60 })).toBe('1,180 in · 60 out tokens');
    expect(tokensLabel({ promptTokens: null, completionTokens: null })).toBeNull();
    expect(tokensLabel({})).toBeNull();
  });
});
