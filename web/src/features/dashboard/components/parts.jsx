import clsx from 'clsx';
import { ChevronRight } from 'lucide-react';
import { Link } from 'react-router-dom';

import Skeleton from '../../../components/ui/Skeleton';
import styles from '../dashboard.module.css';

/** A list row that is a link: leading content, a trailing column, a chevron. */
export function LinkRow({ to, children, trailing }) {
  return (
    <li>
      <Link to={to} className={styles.linkRow}>
        <span className={styles.linkRowMain}>{children}</span>
        {trailing ? <span className={styles.linkRowTrail}>{trailing}</span> : null}
        <ChevronRight className={styles.linkRowChevron} aria-hidden="true" />
      </Link>
    </li>
  );
}

export function ListSkeleton({ rows = 3 }) {
  return (
    <div className={styles.listSkeleton} role="status" aria-label="Loading">
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className={styles.listSkeletonRow}>
          <div style={{ flex: 1, display: 'grid', gap: 7 }}>
            <Skeleton width="55%" height={13} />
            <Skeleton width="32%" height={11} />
          </div>
          <Skeleton width={72} height={22} radius={999} />
        </div>
      ))}
    </div>
  );
}

/** One big number with its label — the count is the API's `totalCount`, null while loading. */
export function CountTile({ label, count, tone, to, detail }) {
  const content = (
    <>
      <span className={styles.tileLabel}>
        <span className={clsx(styles.tileDot, styles[`dot-${tone}`])} aria-hidden="true" />
        {label}
      </span>
      <span className={styles.tileValue}>{count === null ? <Skeleton width={36} height={26} /> : count}</span>
      {detail ? <span className={styles.tileDetail}>{detail}</span> : null}
    </>
  );
  return to ? (
    <Link to={to} className={clsx(styles.tile, styles.tileLink)}>
      {content}
    </Link>
  ) : (
    <div className={styles.tile}>{content}</div>
  );
}
