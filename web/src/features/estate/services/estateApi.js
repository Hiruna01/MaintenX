/**
 * Every call the estate feature makes to the API — buildings, rooms and asset categories.
 * Components never fetch. Reads go through useFetch with the paths below; writes are here.
 *
 * All writes are Admin-only on the API (the same split as the asset registry), and every
 * refusal comes back as its own status: 400 for a bad field, 409 for a building code already
 * in use or for something that still has rooms, assets, reports or classes against it.
 */

import { request } from '../../../services/apiClient';
import { CATEGORIES_PATH, ROOMS_PATH } from '../../assets/services/assetsApi';

export { CATEGORIES_PATH, ROOMS_PATH };

/** GET /api/buildings — every building, ordered by code. Not paginated. */
export const BUILDINGS_PATH = '/api/buildings';

const pathFor = (base, id) => `${base}/${encodeURIComponent(id)}`;

const buildingBody = (values) => ({ name: values.name.trim(), code: values.code.trim() });

const roomBody = (values) => ({
  buildingId: Number(values.buildingId),
  name: values.name.trim(),
  code: values.code.trim(),
  floor: Number(values.floor),
});

const categoryBody = (values) => ({
  name: values.name.trim(),
  defaultWarrantyMonths: Number(values.defaultWarrantyMonths),
});

export const createBuilding = (values) =>
  request(BUILDINGS_PATH, { method: 'POST', body: buildingBody(values) });

export const updateBuilding = (id, values) =>
  request(pathFor(BUILDINGS_PATH, id), { method: 'PUT', body: buildingBody(values) });

/** 409 while the building still has rooms — the API refuses rather than cascading them away. */
export const deleteBuilding = (id) => request(pathFor(BUILDINGS_PATH, id), { method: 'DELETE' });

export const createRoom = (values) => request(ROOMS_PATH, { method: 'POST', body: roomBody(values) });

export const updateRoom = (id, values) =>
  request(pathFor(ROOMS_PATH, id), { method: 'PUT', body: roomBody(values) });

/** 409 while an asset, a report or a timetabled class still names the room — its history outlives it. */
export const deleteRoom = (id) => request(pathFor(ROOMS_PATH, id), { method: 'DELETE' });

export const createCategory = (values) =>
  request(CATEGORIES_PATH, { method: 'POST', body: categoryBody(values) });

/**
 * Changing a category's default warranty changes no existing asset: it is a default for data
 * entry, and every warranty decision reads the asset's own date. Categories have no delete.
 */
export const updateCategory = (id, values) =>
  request(pathFor(CATEGORIES_PATH, id), { method: 'PUT', body: categoryBody(values) });
