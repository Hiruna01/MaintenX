import { ASSET_STATUSES, FIELD_LIMITS, isValidDateOnly } from './assetsApi';

/**
 * The asset form's starting values and its validate(). Kept out of AssetForm.jsx so that
 * file exports only a component, and so the rules can be read without the markup.
 */

export const EMPTY_ASSET_VALUES = {
  assetTag: '',
  name: '',
  assetCategoryId: '',
  roomId: '',
  manufacturer: '',
  model: '',
  installedOn: '',
  warrantyExpiresOn: '',
  status: 'Active',
};

/**
 * Returns an object of per-field messages; an empty object means the form is valid.
 *
 * The limits mirror the DataAnnotations on CreateAssetDto / UpdateAssetDto, so a value
 * the API would reject with a 400 is caught here first. It checks shape only — whether a
 * tag is already in use is the API's question to answer, and it answers 409.
 */
export function validate(values, mode) {
  const errors = {};

  if (mode === 'create') {
    if (!values.assetTag.trim()) {
      errors.assetTag = 'Asset tag is required.';
    } else if (values.assetTag.trim().length > FIELD_LIMITS.assetTag) {
      errors.assetTag = `Asset tag must be ${FIELD_LIMITS.assetTag} characters or fewer.`;
    }
  }

  if (!values.name.trim()) {
    errors.name = 'Name is required.';
  } else if (values.name.trim().length > FIELD_LIMITS.name) {
    errors.name = `Name must be ${FIELD_LIMITS.name} characters or fewer.`;
  }

  if (!values.assetCategoryId) errors.assetCategoryId = 'Choose a category.';
  if (!values.roomId) errors.roomId = 'Choose a room.';

  if (values.manufacturer.trim().length > FIELD_LIMITS.manufacturer) {
    errors.manufacturer = `Manufacturer must be ${FIELD_LIMITS.manufacturer} characters or fewer.`;
  }
  if (values.model.trim().length > FIELD_LIMITS.model) {
    errors.model = `Model must be ${FIELD_LIMITS.model} characters or fewer.`;
  }

  if (!values.installedOn) {
    errors.installedOn = 'Installation date is required.';
  } else if (!isValidDateOnly(values.installedOn)) {
    errors.installedOn = 'Enter a valid date.';
  }

  if (values.warrantyExpiresOn && !isValidDateOnly(values.warrantyExpiresOn)) {
    errors.warrantyExpiresOn = 'Enter a valid date, or leave it blank.';
  }

  if (mode === 'edit' && !ASSET_STATUSES.includes(values.status)) {
    errors.status = 'Choose a status.';
  }

  return errors;
}
