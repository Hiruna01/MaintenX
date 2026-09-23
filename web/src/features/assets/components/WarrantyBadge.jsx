import { formatDateOnly } from '../services/assetsApi';

/**
 * Green when the asset is under warranty, grey when it is not.
 *
 * `isUnderWarranty` comes from the API's failure summary and is NOT recomputed here by
 * comparing the expiry date with today. Warranty dates are a deterministic business rule,
 * so the rule lives in C#; this badge only chooses a colour for the answer. The expiry
 * date is shown beside it so a reader can see what the answer was based on.
 *
 * A null expiry date is "no warranty recorded", which the API reads as not under
 * warranty — the same grey — but it is a different fact from "expired", so it says so.
 */
export function WarrantyBadge({ isUnderWarranty, warrantyExpiresOn, isLoading, hasError }) {
  if (isLoading) {
    return <span className="warranty warranty--pending">Checking warranty…</span>;
  }

  if (hasError || typeof isUnderWarranty !== 'boolean') {
    return <span className="warranty warranty--off">Warranty status unavailable</span>;
  }

  if (isUnderWarranty) {
    return (
      <span className="warranty warranty--on">
        <ShieldIcon />
        Under warranty · until {formatDateOnly(warrantyExpiresOn)}
      </span>
    );
  }

  return (
    <span className="warranty warranty--off">
      <ShieldIcon />
      {warrantyExpiresOn
        ? `Warranty expired · ${formatDateOnly(warrantyExpiresOn)}`
        : 'No warranty recorded'}
    </span>
  );
}

function ShieldIcon() {
  return (
    <svg className="warranty__icon" viewBox="0 0 16 16" aria-hidden="true">
      <path
        d="M8 1.5 2.75 3.5v4.1c0 3.2 2.2 5.9 5.25 6.9 3.05-1 5.25-3.7 5.25-6.9V3.5L8 1.5Z"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.4"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export default WarrantyBadge;
