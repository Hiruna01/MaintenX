import { BriefcaseBusiness, ChevronRight, Mail } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';

import { formatInstant, timeAgo } from '../../../components/ui/format';
import { Pill, StatusPill } from '../../../components/ui/Pill';
import rows from '../../../components/ui/rows.module.css';
import { roleLabel } from '../../auth/services/roles';
import AccountStatusPill from './AccountStatusPill';

/**
 * The accounts as floating rows. Presentational — it is handed a page of users and fetches
 * nothing. A click opens the manage panel over the list (/users/:id); the name is also a real
 * link, so Cmd/Ctrl-click and keyboard navigation work.
 *
 * `liveWorkOrderCount` is the API's count of unfinished orders assigned to the person, shown
 * so an Admin can see before trying why a Technician cannot be switched off yet.
 */
export function UserRows({ users, currentUserId }) {
  const navigate = useNavigate();

  return (
    <div className={rows.wrap}>
      <table className={rows.table}>
        <caption className="mx-visually-hidden">User accounts</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Person</th>
            <th scope="col">Role</th>
            <th scope="col">Account</th>
            <th scope="col">Created</th>
            <th scope="col">
              <span className="mx-visually-hidden">Open</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {users.map((user, index) => (
            <tr
              key={user.id}
              className={rows.row}
              style={{ animationDelay: `${Math.min(index, 10) * 28}ms` }}
              onClick={(event) => {
                if (event.target.closest('a')) return;
                navigate(`/users/${user.id}`);
              }}
            >
              <td className={rows.id}>#{user.id}</td>
              <td className={rows.main}>
                <Link className={rows.title} to={`/users/${user.id}`}>
                  {user.fullName}
                  {user.id === currentUserId ? ' (you)' : ''}
                </Link>
                <span className={rows.sub}>
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                    <Mail aria-hidden="true" size={13} />
                    {user.email}
                  </span>
                </span>
              </td>
              <td>
                <div className={rows.stack}>
                  <StatusPill status={user.role} label={roleLabel(user.role)} />
                  {user.liveWorkOrderCount > 0 ? (
                    <Pill tone="blue" icon={BriefcaseBusiness}>
                      {user.liveWorkOrderCount} live {user.liveWorkOrderCount === 1 ? 'job' : 'jobs'}
                    </Pill>
                  ) : null}
                </div>
              </td>
              <td>
                <AccountStatusPill isActive={user.isActive} />
              </td>
              <td>
                <span className={rows.time}>{formatInstant(user.createdAt)}</span>
                <span className={rows.timeSub}>{timeAgo(user.createdAt)}</span>
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

export default UserRows;
