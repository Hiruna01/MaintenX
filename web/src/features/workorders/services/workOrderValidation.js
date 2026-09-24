/**
 * The work order forms' validate() functions. Each returns `{ field: message }` and an empty
 * object when the values are fine — the same shape as assetValidation.js, so every form in
 * the app reports errors the same way.
 *
 * The limits mirror the DataAnnotations on the API's input DTOs. They are there so a mistake
 * is shown before a round trip; the API checks every one of them again and its 400 is shown
 * if it disagrees.
 */

/** RejectWorkOrderDto.Reason and RequestRevisionDto.Note: [Required], [MaxLength(1000)]. */
export const DECISION_NOTE_MAX = 1000;

/** CompleteWorkOrderDto: ResolutionNote [StringLength(2000, MinimumLength = 20)]. */
export const RESOLUTION_NOTE_MIN = 20;
export const RESOLUTION_NOTE_MAX = 2000;

/** CompleteWorkOrderDto: CompletionPhotoUrl [MaxLength(500)]. */
export const PHOTO_URL_MAX = 500;

/** CompleteWorkOrderDto.ActualCost: the decimal [Range] "0" to "10000000". */
export const MAX_COST = 10_000_000;

/** GET slots/available: durationMinutes [Range(15, 1440)]. */
export const DURATION_MIN = 15;
export const DURATION_MAX = 1440;

/** A reject reason or a revision note: required, not blank, and within the cap. */
export function validateDecisionNote(value, label) {
  const trimmed = value.trim();
  if (trimmed === '') return { note: `${label} is required.` };
  if (trimmed.length > DECISION_NOTE_MAX) {
    return { note: `${label} must be ${DECISION_NOTE_MAX} characters or fewer.` };
  }
  return {};
}

export const EMPTY_COMPLETION_VALUES = {
  actualCost: '',
  outcome: '',
  resolutionNote: '',
  completionPhotoUrl: '',
};

/**
 * Money is checked as TEXT — digits and at most two decimal places — rather than by parsing
 * it into a float and asking whether it looks right. Rupees and cents, nothing finer.
 */
const MONEY_PATTERN = /^\d+(\.\d{1,2})?$/;

export function validateCompletion(values) {
  const errors = {};
  const cost = values.actualCost.trim();

  if (cost === '') {
    // Required: a missing cost is a 400 on the API, never a job recorded as free.
    errors.actualCost = 'Enter what the work actually cost.';
  } else if (!MONEY_PATTERN.test(cost)) {
    errors.actualCost = 'Enter an amount in rupees, with at most two decimal places.';
  } else if (Number(cost) > MAX_COST) {
    errors.actualCost = 'That is more than any single repair could cost — check for a typo.';
  }

  if (!values.outcome) {
    // Required: an outcome left to default would record a temporary fix as resolved.
    errors.outcome = 'Choose how the job ended.';
  }

  const note = values.resolutionNote.trim();
  if (note.length < RESOLUTION_NOTE_MIN) {
    errors.resolutionNote = `Say what was done — at least ${RESOLUTION_NOTE_MIN} characters. This note goes into the asset's service history.`;
  } else if (values.resolutionNote.length > RESOLUTION_NOTE_MAX) {
    errors.resolutionNote = `Keep it to ${RESOLUTION_NOTE_MAX} characters or fewer.`;
  }

  const photoUrl = values.completionPhotoUrl.trim();
  if (photoUrl.length > PHOTO_URL_MAX) {
    errors.completionPhotoUrl = `The link must be ${PHOTO_URL_MAX} characters or fewer.`;
  } else if (photoUrl !== '' && !/^https?:\/\//i.test(photoUrl)) {
    errors.completionPhotoUrl = 'Paste a full link starting with http:// or https://.';
  }

  return errors;
}

/**
 * The slot search. Only the input's shape is checked here — the 31-day cap and whether the
 * job fits in a working day are the API's rules, and its 400 says so when one is broken.
 * "YYYY-MM-DD" strings compare correctly as strings, so no Date object is involved.
 */
export function validateSlotSearch(values) {
  const errors = {};
  const duration = Number(values.durationMinutes);

  if (!Number.isInteger(duration) || duration < DURATION_MIN || duration > DURATION_MAX) {
    errors.durationMinutes = `Between ${DURATION_MIN} and ${DURATION_MAX} whole minutes.`;
  }

  if (!values.fromDate) errors.fromDate = 'Choose a first day.';
  if (!values.toDate) errors.toDate = 'Choose a last day.';

  if (values.fromDate && values.toDate && values.fromDate > values.toDate) {
    errors.toDate = 'The last day is before the first.';
  }

  return errors;
}
