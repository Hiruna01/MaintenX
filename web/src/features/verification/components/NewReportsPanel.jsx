import { Link } from 'react-router-dom';

import ReportStatusBadge from '../../reports/components/ReportStatusBadge';
import { formatDateTime } from '../services/verificationApi';

/**
 * Reports filed against the same asset after the repair was completed — somebody seeing the
 * fault again. Chosen by the API (same asset, filed after CompletedAt, the original report
 * left out); this only lists them, oldest first, with each description verbatim.
 *
 * Rendered for a manager only. A Reporter's copy of the check carries null here, because these
 * are other people's reports, and the page leaves the section out rather than say "none".
 */
export function NewReportsPanel({ reports }) {
  if (reports.length === 0) {
    return (
      <p className="work-order-panel__note">
        Nobody has reported a fault on this asset since the repair was completed.
      </p>
    );
  }

  return (
    <ol className="related-list">
      {reports.map((report) => (
        <li key={report.id} className="related-list__item">
          <div className="related-list__head">
            <Link to={`/reports/${report.id}`}>Report #{report.id}</Link>
            <ReportStatusBadge status={report.status} />
            <span className="related-list__when">{formatDateTime(report.createdAt)}</span>
          </div>
          <p className="related-list__text">{report.description}</p>
        </li>
      ))}
    </ol>
  );
}

export default NewReportsPanel;
