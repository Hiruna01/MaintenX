import { CalendarDays } from 'lucide-react';

import styles from './ui.module.css';

/**
 * A from–to pair of calendar dates in one pill. Values are the "YYYY-MM-DD" strings an
 * <input type="date"> produces, passed straight through — never through `new Date()`.
 */
export function DateRange({ label, from, to, onFromChange, onToChange, invalid = false }) {
  return (
    <div className={styles.dateRange} data-invalid={invalid} role="group" aria-label={label}>
      <CalendarDays aria-hidden="true" />
      <span className={styles.dateRangeLabel}>{label}</span>
      <input type="date" value={from} onChange={(event) => onFromChange(event.target.value)} aria-label={`${label} from`} max={to || undefined} />
      <span aria-hidden="true">–</span>
      <input type="date" value={to} onChange={(event) => onToChange(event.target.value)} aria-label={`${label} to`} min={from || undefined} />
    </div>
  );
}

export default DateRange;
