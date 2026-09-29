import { Link } from 'react-router-dom';

import TagChip from '../../assets/components/TagChip';
import WarrantyPill from '../../assets/components/WarrantyPill';
import { formatDateOnly } from '../../assets/services/assetsApi';
import { formatMoney } from '../../workorders/services/workOrdersApi';
import { enumLabel } from '../services/verificationApi';
import styles from '../verification.module.css';

/**
 * Assets with three or more service visits in the 90-day window, ranked by cost as the API
 * ranks them — the list a manager acts on. Every figure is the API's; the rank is only the
 * row's position.
 *
 * The cost is what is KNOWN to have been spent: visits with no work order behind them carry
 * no cost, and the count of those sits beside the total so a Rs 0 machine is not read as free.
 */
export function RepeatFailureTable({ failures }) {
  return (
    <ol className={styles.ranked}>
      {failures.map((asset, index) => (
        <li key={asset.assetId} className={styles.rankedRow}>
          <span className={styles.rank}>{String(index + 1).padStart(2, '0')}</span>

          <div className={styles.rankedMain}>
            <Link to={`/assets/${asset.assetId}`} className={styles.rankedName}>
              {asset.name}
            </Link>
            <span className={styles.rankedSub}>
              <TagChip tag={asset.assetTag} />
              <span>{asset.categoryName}</span>
              {asset.status !== 'Active' ? <span>{enumLabel(asset.status)}</span> : null}
            </span>
          </div>

          <div className={styles.rankedFigure}>
            <span className={styles.rankedValue}>{asset.failureCount}</span>
            <span className={styles.rankedLabel}>visits · 90 d</span>
          </div>

          <div className={styles.rankedFigure}>
            <span className={styles.rankedValueSm}>{formatDateOnly(asset.lastServicedOn)}</span>
            <span className={styles.rankedLabel}>{asset.daysSinceLastService} days ago</span>
          </div>

          <div className={styles.rankedFigure}>
            <span className={styles.rankedValueSm}>{formatMoney(asset.totalCost)}</span>
            <span className={styles.rankedLabel}>
              {asset.visitsWithoutCost > 0
                ? `+${asset.visitsWithoutCost} visit${asset.visitsWithoutCost === 1 ? '' : 's'} uncosted`
                : 'known cost'}
            </span>
          </div>

          <div className={styles.rankedWarranty}>
            <WarrantyPill isUnderWarranty={asset.isUnderWarranty} warrantyExpiresOn={asset.warrantyExpiresOn} />
          </div>
        </li>
      ))}
    </ol>
  );
}

export default RepeatFailureTable;
