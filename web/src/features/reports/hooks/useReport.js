import useFetch from '../../../hooks/useFetch';
import { buildReportPath } from '../services/reportsApi';

/**
 * One report from GET /api/reports/{id}: the report, its clarification questions with any
 * answers, and every agent step recorded for it OLDEST FIRST. Nothing on the client
 * re-sorts the steps — the order they happened in is the point of an audit trail.
 */
export function useReport(id) {
  return useFetch(buildReportPath(id));
}

export default useReport;
