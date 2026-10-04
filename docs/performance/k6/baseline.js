// Baseline: each read endpoint on its own, one user, no load — the fastest it can answer.
// 10 warm-up requests per endpoint (not counted), then ITERATIONS measured ones.
//   k6 run -e ITERATIONS=100 baseline.js
import http from 'k6/http';
import { check } from 'k6';
import { Trend, Rate } from 'k6/metrics';
import { BASE_URL, READS, TREND_STATS, auth, login } from './common.js';

const ITERATIONS = Number(__ENV.ITERATIONS || 100);
const trends = Object.fromEntries(READS.map(([name]) => [name, new Trend(`ep_${name}`, true)]));
const failed = new Rate('ep_failed');

export const options = {
  scenarios: { baseline: { executor: 'per-vu-iterations', vus: 1, iterations: 1, maxDuration: '20m' } },
  summaryTrendStats: TREND_STATS,
};

export function setup() {
  return { manager: login('manager'), reporter: login('reporter') };
}

export default function (tokens) {
  for (const [name, role, path] of READS) {
    const params = role ? auth(tokens[role]) : {};
    for (let i = 0; i < 10 + ITERATIONS; i++) {
      const res = http.get(`${BASE_URL}${path}`, { ...params, tags: { name } });
      const ok = check(res, { '200': (r) => r.status === 200 });
      if (i >= 10) {
        trends[name].add(res.timings.duration);
        failed.add(!ok);
      }
    }
  }
}

export function handleSummary(data) {
  const out = __ENV.OUT || 'baseline-summary.json';
  return { [out]: JSON.stringify(data, null, 1), stdout: '\n' };
}
