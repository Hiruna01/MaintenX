import clsx from 'clsx';
import { CircleX, RotateCcw } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Panel } from '../../../components/ui/Panel';
import { toneFor } from '../../../components/ui/tones';
import { LIFECYCLE, lifecyclePosition, shortStateLabel } from '../services/lifecycle';
import styles from '../workflows.module.css';

/**
 * The lifecycle drawn as a rail, with the workflow's current state lit.
 *
 * It shows WHERE the state sits, nothing more: every other stage is drawn the same neutral
 * way, because a run can skip clarification and the rail must not claim a stage happened.
 * Failed hangs off the end as its own branch. A reopened run marks the loop back to
 * Diagnosing — the verification loop sent it there when a repair did not hold.
 */
export function LifecycleRail({ state, reopenedWorkOrderId }) {
  const position = lifecyclePosition(state);
  const failed = state === 'Failed';
  const tone = toneFor(state);

  return (
    <Panel eyebrow="Lifecycle" actions={<span className={styles.panelHint}>Where this run is now</span>}>
      <div className={styles.railScroll}>
        <ol className={clsx(styles.rail, styles[`tone-${tone}`])}>
          {LIFECYCLE.map((stage, index) => {
            const isNow = index === position;
            return (
              <li key={stage} className={clsx(styles.railStop, isNow && styles.railStopNow)} aria-current={isNow ? 'step' : undefined}>
                <span className={styles.railNode} aria-hidden="true" />
                <span className={styles.railLabel}>{shortStateLabel(stage)}</span>
                {isNow ? <span className={styles.railNowTag}>Now</span> : null}
                {stage === 'Diagnosing' && reopenedWorkOrderId ? (
                  <Link to={`/workorders/${reopenedWorkOrderId}`} className={styles.railLoop}>
                    <RotateCcw aria-hidden="true" />
                    Reopened · WO #{reopenedWorkOrderId}
                  </Link>
                ) : null}
              </li>
            );
          })}
          <li className={clsx(styles.railStop, styles.railBranch, failed && styles.railStopNow)} aria-current={failed ? 'step' : undefined}>
            <span className={clsx(styles.railNode, styles.railNodeFailed)} aria-hidden="true">
              <CircleX />
            </span>
            <span className={styles.railLabel}>Failed</span>
            {failed ? <span className={styles.railNowTag}>Now</span> : null}
          </li>
        </ol>
      </div>
    </Panel>
  );
}

export default LifecycleRail;
