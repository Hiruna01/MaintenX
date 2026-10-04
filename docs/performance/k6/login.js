// Sign-in under concurrency. Every login verifies a PBKDF2 password hash, which is slow on
// purpose, so this is the most CPU-heavy request the API serves. Needs a raised sign-in
// limit (RATE_LIMIT_AUTH_PER_MINUTE) or every request after the 10th is a 429.
//   k6 run -e VUS=10 -e DURATION=30s login.js
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, TREND_STATS } from './common.js';

const VUS = Number(__ENV.VUS || 10);
export const options = {
  scenarios: { login: { executor: 'constant-vus', vus: VUS, duration: __ENV.DURATION || '30s' } },
  summaryTrendStats: TREND_STATS,
  thresholds: { http_req_failed: ['rate<0.01'] },
};

const body = JSON.stringify({ email: 'manager@campus.test', password: __ENV.MX_PW_FACILITIESMANAGER });

export default function () {
  const res = http.post(`${BASE_URL}/api/auth/login`, body, { headers: { 'Content-Type': 'application/json' } });
  check(res, { '200': (r) => r.status === 200 });
}

export function handleSummary(data) {
  return { [__ENV.OUT || `login-${VUS}vus-summary.json`]: JSON.stringify(data, null, 1), stdout: '\n' };
}
