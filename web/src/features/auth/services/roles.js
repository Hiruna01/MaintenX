/**
 * Mirrors the API's `Role` enum. The API serialises enums by NAME on both sides
 * (JsonStringEnumConverter), so these are the exact strings that travel in JSON and sit
 * in the `role` claim of the JWT — a client never hardcodes an enum's integer value.
 */
export const ROLES = {
  Reporter: 'Reporter',
  Technician: 'Technician',
  FacilitiesManager: 'FacilitiesManager',
  Admin: 'Admin',
};

/** Everyone who may act on the maintenance queue rather than only report into it. */
export const MANAGER_ROLES = [ROLES.FacilitiesManager, ROLES.Admin];

/**
 * Who may change the asset registry. Matches the API's per-action
 * `[Authorize(Policy = nameof(Role.Admin))]` — deciding what equipment the estate contains
 * is an Admin's job, and there is no "or anyone more senior" fallback in either place.
 */
export const ADMIN_ROLES = [ROLES.Admin];

/**
 * Who works the dispatch board. A Technician reads their own queue there and a manager reads
 * the estate — WHICH orders each sees is decided by the API from the token, not by this list.
 * A Reporter's view of a fault is the report, so they have no work orders to read.
 */
export const WORK_ORDER_ROLES = [ROLES.Technician, ROLES.FacilitiesManager, ROLES.Admin];

/**
 * Who raises, assigns, schedules and DECIDES work orders — the approval queue included.
 * Matches the API's `FacilitiesManager` policy on those actions exactly: an Admin is refused
 * there too, because the policies are one-per-role and carry no seniority, so an Admin is not
 * offered a link or a button that would only come back 403. A Technician never sees the
 * approvals link at all.
 */
export const DISPATCH_ROLES = [ROLES.FacilitiesManager];

const ROLE_LABELS = {
  [ROLES.Reporter]: 'Reporter',
  [ROLES.Technician]: 'Technician',
  [ROLES.FacilitiesManager]: 'Facilities Manager',
  [ROLES.Admin]: 'Admin',
};

export function roleLabel(role) {
  return ROLE_LABELS[role] ?? role ?? 'Unknown';
}

/** `allowedRoles` undefined or empty means "any signed-in user". */
export function hasRole(role, allowedRoles) {
  if (!allowedRoles || allowedRoles.length === 0) return true;
  return allowedRoles.includes(role);
}
