import { enumLabel } from '../services/reportsApi';

/**
 * A report's status as a pill. The modifier class is the enum NAME the API sent
 * ("AwaitingClarification"), never an ordinal, so the colour cannot drift from the meaning.
 */
export function ReportStatusBadge({ status }) {
  return (
    <span className={`report-status report-status--${status}`}>
      <span className="asset-status__dot" aria-hidden="true" />
      {enumLabel(status)}
    </span>
  );
}

export default ReportStatusBadge;
