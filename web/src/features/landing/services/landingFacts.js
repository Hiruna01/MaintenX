/**
 * The rule numbers the landing page quotes. They are COPY, not rules: the page is signed-out
 * and reads no configuration, and nothing here decides anything. Each is the API's default —
 * if one of those defaults changes, change its line here too, or the page states a rule the
 * system no longer follows.
 */
export const LANDING_FACTS = [
  // VerificationSettings.DelayDays (Verification:DelayDays), default 5.
  { value: '5 days', label: 'before we ask whether a repair held' },
  // ApprovalSettings.CostThreshold (Approval:CostThreshold), default 15000 LKR.
  { value: 'Rs 15k', label: 'most spent without a manager’s decision' },
  // ClarifierOutput caps the questions at two (agent/schemas.py).
  { value: '2', label: 'questions at most, per report' },
  // SchedulingSettings.ClassBufferMinutes (Scheduling:ClassBufferMinutes), default 15.
  { value: '15 min', label: 'kept clear either side of every lecture' },
  // FailureRules: the 90-day repeat-failure window.
  { value: '90 days', label: 'window that makes a repeat failure' },
];

/** The seeded projector every demo starts from. Its sticker on the page is a real, scannable QR. */
export const DEMO_ASSET_TAG = 'PRJ-MAB101-01';
