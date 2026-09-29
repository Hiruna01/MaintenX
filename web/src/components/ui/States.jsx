import clsx from 'clsx';
import { AlertTriangle, Inbox } from 'lucide-react';

import styles from './ui.module.css';

/** Nothing to show. Not a failure, and it never looks like one — it says what to do next. */
export function EmptyState({ icon: Icon = Inbox, title, body, action, compact = false }) {
  return (
    <div className={clsx(styles.state, compact && styles.stateCompact)}>
      <span className={styles.stateIcon} aria-hidden="true">
        <Icon strokeWidth={1.7} />
      </span>
      <p className={styles.stateTitle}>{title}</p>
      {body ? <p className={styles.stateBody}>{body}</p> : null}
      {action ? <div className={styles.stateAction}>{action}</div> : null}
    </div>
  );
}

/** A request failed. Says what failed and carries the API's own message. */
export function ErrorState({ title, message, compact = false, action }) {
  return (
    <div className={clsx(styles.state, styles.stateError, compact && styles.stateCompact)} role="alert">
      <span className={styles.stateIcon} aria-hidden="true">
        <AlertTriangle strokeWidth={1.7} />
      </span>
      <p className={styles.stateTitle}>{title}</p>
      {message ? <p className={styles.stateBody}>{message}</p> : null}
      {action ? <div className={styles.stateAction}>{action}</div> : null}
    </div>
  );
}

export default EmptyState;
