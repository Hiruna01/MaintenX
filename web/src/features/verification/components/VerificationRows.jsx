import clsx from 'clsx';
import { ChevronRight } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';

import { formatInstant, timeAgo } from '../../../components/ui/format';
import { StatusPill } from '../../../components/ui/Pill';
import rows from '../../../components/ui/rows.module.css';
import TagChip from '../../assets/components/TagChip';
import { enumLabel } from '../services/verificationApi';
import styles from '../verification.module.css';
import { AnswerPill, OverduePill } from './VerificationPills';

/**
 * The checks as floating rows. Presentational — it is handed a page and fetches nothing.
 *
 * Each row leads with the fault as reported, because a reporter is not expected to know an
 * asset tag and the question is "is THAT fixed?". An overdue row is the API's `isOverdue`:
 * a pill and a red edge, never a date compared in the browser.
 */
export function VerificationRows({ checks }) {
  const navigate = useNavigate();

  return (
    <div className={rows.wrap}>
      <table className={rows.table}>
        <caption className="mx-visually-hidden">Verification checks</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Repair</th>
            <th scope="col">Status</th>
            <th scope="col">Reporter says</th>
            <th scope="col">Due</th>
            <th scope="col">
              <span className="mx-visually-hidden">Open</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {checks.map((check, index) => (
            <tr
              key={check.id}
              className={clsx(rows.row, check.isOverdue && styles.rowOverdue)}
              style={{ animationDelay: `${Math.min(index, 10) * 28}ms` }}
              onClick={(event) => {
                if (event.target.closest('a')) return;
                navigate(`/verifications/${check.id}`);
              }}
            >
              <td className={rows.id}>#{check.id}</td>
              <td className={rows.main}>
                <Link className={rows.title} to={`/verifications/${check.id}`}>
                  {check.reportDescription}
                </Link>
                <span className={rows.sub}>
                  <TagChip tag={check.assetTag} />
                  <span>Work order #{check.workOrderId}</span>
                  {check.workOrderCompletedAt ? <span>Repaired {timeAgo(check.workOrderCompletedAt)}</span> : null}
                </span>
              </td>
              <td>
                <div className={rows.stack}>
                  <StatusPill status={check.status} label={enumLabel(check.status)} />
                  {check.isOverdue ? <OverduePill status={check.status} /> : null}
                </div>
              </td>
              <td>
                <AnswerPill confirmed={check.reporterConfirmed} />
              </td>
              <td>
                <span className={rows.time}>{formatInstant(check.dueAt)}</span>
                <span className={rows.timeSub}>{timeAgo(check.dueAt)}</span>
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

export default VerificationRows;
