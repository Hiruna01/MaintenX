// Concurrent load: VUS users each sending reads back to back (no think time) for DURATION,
// in the weighted mix below. Run once per concurrency level:
//   k6 run -e VUS=25 -e DURATION=60s load.js
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, READS, TREND_STATS, auth, login } from './common.js';

// Rough share of each read in a working day: lists and asset look-ups dominate.
const WEIGHTS = {
  health: 2, assets_list: 12, assets_search: 10, asset_detail: 10, asset_failure_summary: 8,
  asset_by_tag: 6, reports_list_manager: 10, reports_list_reporter: 6, report_detail: 6,
  workorders_list: 10, approval_queue: 5, workflow_detail: 4, verifications_list: 5,
  analytics_metrics: 4, agent_metrics: 2,
};
const BAG = READS.flatMap((r) => Array(WEIGHTS[r[0]]).fill(r));

const VUS = Number(__ENV.VUS || 10);
export const options = {
  scenarios: { load: { executor: 'constant-vus', vus: VUS, duration: __ENV.DURATION || '60s' } },
  summaryTrendStats: TREND_STATS,
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500'],
    // Per-request-type sub-metrics, so the summary breaks the latency down.
    ...Object.fromEntries(READS.map(([name]) => [`http_req_duration{name:${name}}`, ['p(95)<5000']])),
  },
};

export function setup() {
  return { manager: login('manager'), reporter: login('reporter') };
}

export default function (tokens) {
  const [name, role, path] = BAG[Math.floor(Math.random() * BAG.length)];
  const res = http.get(`${BASE_URL}${path}`, { ...(role ? auth(tokens[role]) : {}), tags: { name } });
  check(res, { '200': (r) => r.status === 200 });
}

export function handleSummary(data) {
  const out = __ENV.OUT || `load-${VUS}vus-summary.json`;
  return { [out]: JSON.stringify(data, null, 1), stdout: '\n' };
}
