/** Every call the assets feature makes to the API lives here — components never fetch. */

import { request } from '../../../services/apiClient';

/**
 * Mirrors the API's `AssetStatus` enum. The API sends and binds enums by NAME
 * (JsonStringEnumConverter), which is also what Postgres stores — so no ordinal is ever
 * hardcoded here, and a member inserted into the C# enum cannot shift the client's meaning.
 */
export const ASSET_STATUSES = ['Active', 'UnderMaintenance', 'Retired'];

/** Mirrors the API's `ServiceOutcome` enum, by NAME. */
export const SERVICE_OUTCOMES = ['Resolved', 'TemporaryFix', 'PartReplaced', 'NoFaultFound'];

/**
 * Mirrors the API's `AssetSort` query enum. There are exactly two orders and both are
 * ascending — the API takes no direction — so the table offers these two and nothing else.
 */
export const ASSET_SORTS = {
  Name: 'Name',
  InstalledOn: 'InstalledOn',
};

export const DEFAULT_PAGE_SIZE = 10;

/** Matches the DataAnnotations on CreateAssetDto / UpdateAssetDto. */
export const FIELD_LIMITS = {
  assetTag: 50,
  name: 200,
  manufacturer: 100,
  model: 100,
};

/** Turns "UnderMaintenance" / "NoFaultFound" into "Under Maintenance" / "No Fault Found". */
export function enumLabel(value) {
  return String(value ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
}

/**
 * Formats an API `DateOnly` ("2026-07-03") for display.
 *
 * Deliberately NOT `new Date("2026-07-03")`: that parses as UTC midnight, and in any
 * timezone west of Greenwich it renders as the day before. The API made these dates
 * `DateOnly` precisely so no timezone could move them across midnight, so the parts are
 * read directly and a local date is built from them.
 */
export function formatDateOnly(value, options = { day: 'numeric', month: 'short', year: 'numeric' }) {
  const parts = parseDateOnly(value);
  if (!parts) return '—';
  return new Date(parts.year, parts.month - 1, parts.day).toLocaleDateString(undefined, options);
}

function parseDateOnly(value) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value ?? ''));
  if (!match) return null;
  return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
}

/** True for a real calendar date in the "YYYY-MM-DD" shape an <input type="date"> produces. */
export function isValidDateOnly(value) {
  const parts = parseDateOnly(value);
  if (!parts) return false;
  const date = new Date(Date.UTC(parts.year, parts.month - 1, parts.day));
  return date.getUTCMonth() === parts.month - 1 && date.getUTCDate() === parts.day;
}

/**
 * `installedOn` plus a category's default warranty, as "YYYY-MM-DD" — a data-entry
 * convenience for the create form, and nothing more. The Admin sees the result and can
 * change it; what every warranty decision reads is the asset's own stored date, and
 * whether an asset is under warranty is computed by the API, never here.
 */
export function addMonthsToDateOnly(value, months) {
  const parts = parseDateOnly(value);
  if (!parts) return '';
  const date = new Date(Date.UTC(parts.year, parts.month - 1 + months, parts.day));
  return date.toISOString().slice(0, 10);
}

/**
 * Builds the path for GET /api/assets. Returned as a string rather than fetched here,
 * because the page reads it through useFetch — which owns loading, error and the JWT.
 * Empty filters are left out entirely rather than sent blank.
 */
export function buildAssetsPath({
  search = '',
  categoryId = '',
  roomId = '',
  status = '',
  sort = ASSET_SORTS.Name,
  page = 1,
  pageSize = DEFAULT_PAGE_SIZE,
} = {}) {
  const params = new URLSearchParams();
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));
  params.set('sort', sort);

  if (search.trim()) params.set('search', search.trim());
  if (categoryId) params.set('categoryId', String(categoryId));
  if (roomId) params.set('roomId', String(roomId));
  if (status) params.set('status', status);

  return `/api/assets?${params.toString()}`;
}

/** GET /api/assets/{id} — one asset with its category, room and service history (oldest first). */
export function buildAssetPath(id) {
  return `/api/assets/${encodeURIComponent(id)}`;
}

/** GET /api/assets/{id}/failure-summary — counts and date comparisons computed in C#. */
export function buildFailureSummaryPath(id) {
  return `/api/assets/${encodeURIComponent(id)}/failure-summary`;
}

/** GET /api/assetcategories — every category, ordered by name. Not paginated. */
export const CATEGORIES_PATH = '/api/assetcategories';

/** GET /api/rooms — every room. Not paginated. */
export const ROOMS_PATH = '/api/rooms';

/** "MAB-101 · Lecture Hall A" — how a room reads in a picker or a table cell. */
export function roomLabel(room) {
  if (!room) return '—';
  return room.code ? `${room.code} · ${room.name}` : room.name;
}

/** Blank optional strings go to the API as null, not "". */
function optional(value) {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

/**
 * POST /api/assets — Admin only. 201 with the created AssetDto; 409 when the tag is
 * already in use; 400 for an unknown category or room. Throws ApiError carrying the status.
 */
export function createAsset(values) {
  return request('/api/assets', {
    method: 'POST',
    body: {
      assetTag: values.assetTag.trim(),
      name: values.name.trim(),
      assetCategoryId: Number(values.assetCategoryId),
      roomId: Number(values.roomId),
      manufacturer: optional(values.manufacturer),
      model: optional(values.model),
      installedOn: values.installedOn,
      warrantyExpiresOn: values.warrantyExpiresOn || null,
    },
  });
}

/**
 * PUT /api/assets/{id} — Admin only, 204.
 *
 * The body has NO assetTag, matching UpdateAssetDto: the tag is printed on a sticker and
 * encoded in its QR code, and renaming the row would orphan every sticker already applied.
 */
export function updateAsset(id, values) {
  return request(buildAssetPath(id), {
    method: 'PUT',
    body: {
      name: values.name.trim(),
      assetCategoryId: Number(values.assetCategoryId),
      roomId: Number(values.roomId),
      manufacturer: optional(values.manufacturer),
      model: optional(values.model),
      installedOn: values.installedOn,
      warrantyExpiresOn: values.warrantyExpiresOn || null,
      status: values.status,
    },
  });
}
