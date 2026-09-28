import { ChevronRight, MapPin, MessageCircleQuestion } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';

import { formatInstant, timeAgo } from '../../../components/ui/format';
import { Pill, StatusPill } from '../../../components/ui/Pill';
import rows from '../../../components/ui/rows.module.css';
import { humanize, toneFor } from '../../../components/ui/tones';

/**
 * The intake queue as floating rows. Presentational — it is handed a page of reports and
 * fetches nothing. The description is clamped to two lines because this is a worklist; the
 * detail page shows it whole.
 *
 * `verification` is the newest repair check on any order raised for the report (null when no
 * repair has completed) — the report's own status cannot say whether the repair held.
 */
export function ReportRows({ reports }) {
  const navigate = useNavigate();

  return (
    <div className={rows.wrap}>
      <table className={rows.table}>
        <caption className="mx-visually-hidden">Maintenance reports</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Fault</th>
            <th scope="col">Status</th>
            <th scope="col">Reported</th>
            <th scope="col">
              <span className="mx-visually-hidden">Open</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {reports.map((report, index) => (
            <tr
              key={report.id}
              className={rows.row}
              style={{ animationDelay: `${Math.min(index, 10) * 28}ms` }}
              onClick={(event) => {
                if (event.target.closest('a')) return;
                navigate(`/reports/${report.id}`);
              }}
            >
              <td className={rows.id}>#{report.id}</td>
              <td className={rows.main}>
                <Link className={rows.title} to={`/reports/${report.id}`}>
                  {report.description}
                </Link>
                <span className={rows.sub}>
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                    <MapPin aria-hidden="true" size={13} />
                    {report.roomName}
                  </span>
                  {report.assetId ? <span>Asset #{report.assetId} identified</span> : null}
                </span>
              </td>
              <td>
                <div className={rows.stack}>
                  <StatusPill status={report.status} />
                  {/* "Waiting on the reporter" — a count the API computed. */}
                  {report.unansweredQuestionCount > 0 ? (
                    <Pill tone="amber" icon={MessageCircleQuestion}>
                      {report.unansweredQuestionCount} unanswered
                    </Pill>
                  ) : null}
                  {report.verification ? (
                    <Pill tone={toneFor(report.verification.status)} dot={false}>
                      Check · {humanize(report.verification.status)}
                    </Pill>
                  ) : null}
                </div>
              </td>
              <td>
                <span className={rows.time}>
                  {formatInstant(report.createdAt)}
                </span>
                <span className={rows.timeSub}>{timeAgo(report.createdAt)}</span>
              </td>
              <td className={rows.chevron} aria-hidden="true">
                <ChevronRight />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default ReportRows;
