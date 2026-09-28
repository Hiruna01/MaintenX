import clsx from 'clsx';

import { toneFor } from '../../../components/ui/tones';
import { LIFECYCLE, lifecyclePosition, shortStateLabel } from '../services/lifecycle';
import styles from '../workflows.module.css';

/**
 * A compact dot track for a list row: where the current state sits along the lifecycle.
 * A failed run shows an empty track ending in a red mark — it has no place on the line.
 */
export function LifecycleMeter({ state }) {
  const position = lifecyclePosition(state);
  const failed = position === -1;
  const tone = toneFor(state);

  return (
    <span
      className={clsx(styles.meter, styles[`tone-${tone}`])}
      role="img"
      aria-label={
        failed ? 'Failed — off the lifecycle' : `Stage ${position + 1} of ${LIFECYCLE.length}: ${shortStateLabel(state)}`
      }
    >
      {LIFECYCLE.map((stage, index) => (
        <span
          key={stage}
          className={clsx(
            styles.meterDot,
            !failed && index < position && styles.meterDotBefore,
            !failed && index === position && styles.meterDotNow,
          )}
        />
      ))}
      {failed ? <span className={clsx(styles.meterDot, styles.meterDotFailed)} /> : null}
    </span>
  );
}

export default LifecycleMeter;
