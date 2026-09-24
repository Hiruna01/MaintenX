import { enumLabel } from '../services/verificationApi';

/**
 * A check's status as a pill. The modifier class is the enum NAME the API sent
 * ("AwaitingReporterResponse"), never an ordinal, so the colour cannot drift from the meaning.
 */
export function VerificationStatusBadge({ status }) {
  return (
    <span className={`verification-status verification-status--${status}`}>
      <span className="asset-status__dot" aria-hidden="true" />
      {enumLabel(status)}
    </span>
  );
}

export default VerificationStatusBadge;
