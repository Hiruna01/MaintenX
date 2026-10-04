import useFetch from '../../../hooks/useFetch';
import { buildAgentMetricsPath } from '../services/agentMetricsApi';

/**
 * GET /api/analytics/agents. One request feeds every panel on the page, and each panel renders
 * its own loading, empty and error state from this one `{ data, isLoading, error }`.
 */
export function useAgentMetrics(range) {
  return useFetch(buildAgentMetricsPath(range));
}

export default useAgentMetrics;
