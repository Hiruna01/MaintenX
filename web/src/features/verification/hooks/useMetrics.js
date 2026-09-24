import useFetch from '../../../hooks/useFetch';
import { buildMetricsPath } from '../services/verificationApi';

/**
 * GET /api/analytics/metrics. One request feeds every panel on the dashboard, and each panel
 * renders its own loading, empty and error state from this one `{ data, isLoading, error }`.
 */
export function useMetrics(range) {
  return useFetch(buildMetricsPath(range));
}

export default useMetrics;
