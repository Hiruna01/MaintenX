/**
 * validate() for the three estate forms. Each returns a map of field name to message, empty
 * when valid — the same shape as every other form in the client. The limits mirror the
 * DataAnnotations on CreateBuildingDto, CreateRoomDto and CreateAssetCategoryDto; the API
 * checks them again, so these only save a round trip.
 */

function required(errors, values, name, label, max) {
  const value = String(values[name] ?? '').trim();
  if (!value) errors[name] = `${label} is required.`;
  else if (value.length > max) errors[name] = `${label} must be at most ${max} characters.`;
}

function wholeNumber(errors, values, name, label, min, max) {
  const raw = String(values[name] ?? '').trim();
  const number = Number(raw);
  if (raw === '' || !Number.isInteger(number)) errors[name] = `${label} must be a whole number.`;
  else if (number < min || number > max) errors[name] = `${label} must be between ${min} and ${max}.`;
}

export function validateBuilding(values) {
  const errors = {};
  required(errors, values, 'name', 'Name', 200);
  required(errors, values, 'code', 'Code', 20);
  return errors;
}

export function validateRoom(values) {
  const errors = {};
  if (!values.buildingId) errors.buildingId = 'Choose a building.';
  required(errors, values, 'name', 'Name', 200);
  // The code is how a timetable event finds its room (GoogleCalendarSyncService matches the
  // event's location to it), so it is required here as it is on the API.
  required(errors, values, 'code', 'Code', 20);
  wholeNumber(errors, values, 'floor', 'Floor', -5, 200);
  return errors;
}

export function validateCategory(values) {
  const errors = {};
  required(errors, values, 'name', 'Name', 100);
  wholeNumber(errors, values, 'defaultWarrantyMonths', 'Default warranty', 0, 600);
  return errors;
}
