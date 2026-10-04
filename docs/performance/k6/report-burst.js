// A burst of reports filed at the same moment: N reporters each POST one report. Measures
// how fast the API answers 201 (it must not wait for the agents). How long each report then
// waits for its agents is read from the database afterwards (../README.md).
//   k6 run -e N=20 -e TAG=stub report-burst.js
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, TREND_STATS, auth, login } from './common.js';

const N = Number(__ENV.N || 20);
export const options = {
  scenarios: { burst: { executor: 'shared-iterations', vus: N, iterations: N, maxDuration: '2m' } },
  summaryTrendStats: TREND_STATS,
};

export function setup() {
  return { reporter: login('reporter') };
}

export default function (tokens) {
  const body = JSON.stringify({
    description: `[perf ${__ENV.TAG || 'burst'} ${__VU}] The projector in the lecture hall keeps switching off during lectures.`,
    roomId: 1,
  });
  const res = http.post(`${BASE_URL}/api/reports`, body, {
    headers: { ...auth(tokens.reporter).headers, 'Content-Type': 'application/json' },
  });
  check(res, { '201': (r) => r.status === 201 });
}

export function handleSummary(data) {
  return { [__ENV.OUT || `burst-${N}-summary.json`]: JSON.stringify(data, null, 1), stdout: '\n' };
}
