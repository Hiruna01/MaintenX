import clsx from 'clsx';

import styles from './ui.module.css';

/**
 * A white card on the concrete canvas. `eyebrow` is the small-caps section label ("KEY
 * METRICS"); `count` sits beside it in mono.
 */
export function Panel({ eyebrow, count, actions, flush = false, as: Tag = 'section', className, children, ...rest }) {
  return (
    <Tag className={clsx(styles.panel, flush && styles.panelFlush, className)} {...rest}>
      {eyebrow || actions ? (
        <header className={styles.panelHead}>
          {eyebrow ? (
            <h2 className={styles.eyebrow}>
              {eyebrow}
              {count !== undefined && count !== null ? <span className={styles.eyebrowCount}>{count}</span> : null}
            </h2>
          ) : (
            <span />
          )}
          {actions}
        </header>
      ) : null}
      {children}
    </Tag>
  );
}

export function Well({ className, children, ...rest }) {
  return (
    <div className={clsx(styles.well, className)} {...rest}>
      {children}
    </div>
  );
}

export default Panel;
