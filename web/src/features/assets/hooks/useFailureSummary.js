import useFetch from '../../../hooks/useFetch';
import { buildFailureSummaryPath } from '../services/assetsApi';

/**
 * GET /api/assets/{id}/failure-summary.
 *
 * Every figure in it — failure counts, the repeat-failure flag, whether the asset is under
 * warranty — is a count or a date comparison computed by the API in C#. The client
 * displays them and never recomputes one: those are deterministic business rules, and a
 * second copy of a rule in JavaScript is a second answer waiting to disagree with the first.
 */
export function useFailureSummary(id) {
  return useFetch(buildFailureSummaryPath(id));
}

export default useFailureSummary;
