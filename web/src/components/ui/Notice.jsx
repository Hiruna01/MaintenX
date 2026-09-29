import { CircleCheck, X } from 'lucide-react';
import { useState } from 'react';

import styles from './ui.module.css';

/** A success notice after an action — what just happened, dismissible. */
export function Notice({ children }) {
  const [hidden, setHidden] = useState(false);
  if (hidden || !children) return null;

  return (
    <p className={styles.notice} role="status">
      <CircleCheck aria-hidden="true" />
      <span>{children}</span>
      <button type="button" className={styles.noticeClose} onClick={() => setHidden(true)} aria-label="Dismiss">
        <X aria-hidden="true" />
      </button>
    </p>
  );
}

export default Notice;
