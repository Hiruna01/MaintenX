import { enumLabel } from '../services/assetsApi';

/** A service visit's outcome as a pill, keyed by the `ServiceOutcome` enum NAME. */
export function OutcomeBadge({ outcome }) {
  return <span className={`outcome outcome--${outcome}`}>{enumLabel(outcome)}</span>;
}

export default OutcomeBadge;
