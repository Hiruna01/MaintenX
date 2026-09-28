import { formatPercent } from '../services/verificationApi';
import styles from '../verification.module.css';

/**
 * The reopen rate per asset category, worst first as the API ranks it. Each rate sits beside
 * the counts it was divided from, so 1 of 1 is not mistaken for 40 of 40. The bar is the API's
 * percentage drawn to scale — nothing is recomputed to draw it.
 */
export function CategoryReopenTable({ categories }) {
  return (
    <div className={styles.categories}>
      <p className={styles.evidenceLabel}>By category · worst first</p>
      <ol className={styles.bars}>
        {categories.map((category) => (
          <li key={category.assetCategoryId} className={styles.bar}>
            <span className={styles.barName}>{category.categoryName}</span>
            <span className={styles.barTrack} aria-hidden="true">
              <span className={styles.barFill} style={{ width: `${Math.min(100, Math.max(0, Number(category.reopenRate)))}%` }} />
            </span>
            <span className={styles.barRate}>{formatPercent(category.reopenRate)}</span>
            <span className={styles.barCounts}>
              {category.reopened} of {category.answered}
            </span>
          </li>
        ))}
      </ol>
    </div>
  );
}

export default CategoryReopenTable;
