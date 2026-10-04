// Shared helpers for the MaintenX k6 scripts. Nothing secret lives here: passwords come from
// environment variables (MX_PW_REPORTER, MX_PW_TECHNICIAN, MX_PW_FACILITIESMANAGER,
// MX_PW_ADMIN), the seed passwords of the database under test. See ../README.md.
import http from 'k6/http';

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:5199';

const ACCOUNTS = {
  reporter: ['reporter@campus.test', 'MX_PW_REPORTER'],
  technician: ['technician@campus.test', 'MX_PW_TECHNICIAN'],
  manager: ['manager@campus.test', 'MX_PW_FACILITIESMANAGER'],
  admin: ['admin@campus.test', 'MX_PW_ADMIN'],
};

// Signs in once and returns the token. Called from setup(), so a test spends its sign-in
// budget once rather than per request (sign-in is rate-limited per address).
export function login(role) {
  const [email, envName] = ACCOUNTS[role];
  const password = __ENV[envName];
  if (!password) throw new Error(`Set ${envName} to the ${role} account's password.`);

  const res = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({ email, password }), {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'setup_login' },
  });
  if (res.status !== 200) throw new Error(`Sign-in as ${role} failed: ${res.status}`);
  return res.json('token');
}

export const auth = (token) => ({ headers: { Authorization: `Bearer ${token}` } });

// The read requests used by the baseline and the mixed load test: [name, role, path].
// Ids are rows that exist in the seeded demo database.
export const READS = [
  ['health', null, '/health'],
  ['assets_list', 'manager', '/api/assets?page=1&pageSize=20'],
  ['assets_search', 'manager', '/api/assets?search=projector&page=1&pageSize=20'],
  ['asset_detail', 'manager', '/api/assets/1'],
  ['asset_failure_summary', 'manager', '/api/assets/1/failure-summary'],
  ['asset_by_tag', 'reporter', '/api/assets/by-tag/PRJ-MAB101-01'],
  ['reports_list_manager', 'manager', '/api/reports?page=1&pageSize=20'],
  ['reports_list_reporter', 'reporter', '/api/reports?page=1&pageSize=20'],
  ['report_detail', 'manager', '/api/reports/23'],
  ['workorders_list', 'manager', '/api/workorders?page=1&pageSize=20'],
  ['approval_queue', 'manager', '/api/workorders/approvals'],
  ['workflow_detail', 'manager', '/api/workflows/18'],
  ['verifications_list', 'manager', '/api/verifications?page=1&pageSize=20'],
  ['analytics_metrics', 'manager', '/api/analytics/metrics'],
  ['agent_metrics', 'manager', '/api/analytics/agents'],
];

export const TREND_STATS = ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)', 'count'];
