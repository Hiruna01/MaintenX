import clsx from 'clsx';

import { humanize, toneFor } from './tones';
import styles from './ui.module.css';

/** A soft status pill. `tone` wins over the tone looked up from `status`. */
export function Pill({ tone, dot = true, icon: Icon, className, children }) {
  return (
    <span className={clsx(styles.pill, styles[`tone-${tone ?? 'slate'}`], className)}>
      {Icon ? <Icon aria-hidden="true" strokeWidth={2} /> : dot ? <span className={styles.pillDot} aria-hidden="true" /> : null}
      {children}
    </span>
  );
}

/** A pill for an enum NAME the API sent — coloured by name, labelled in sentence case. */
export function StatusPill({ status, label }) {
  return <Pill tone={toneFor(status)}>{label ?? humanize(status)}</Pill>;
}

export default Pill;
