import { ChevronRight } from 'lucide-react';
import { Link } from 'react-router-dom';

import { Panel, Well } from '../../../components/ui/Panel';
import styles from '../dashboard.module.css';

/** Shortcuts to the screens this role actually uses. `actions` is [{ to, label, icon }]. */
export function QuickActions({ actions }) {
  return (
    <Panel eyebrow="Quick actions">
      <Well>
        <ul className={styles.actions}>
          {actions.map(({ to, label, icon: Icon }) => (
            <li key={to}>
              <Link to={to} className={styles.action}>
                <Icon aria-hidden="true" strokeWidth={1.7} />
                <span>{label}</span>
                <ChevronRight className={styles.actionChevron} aria-hidden="true" />
              </Link>
            </li>
          ))}
        </ul>
      </Well>
    </Panel>
  );
}

export default QuickActions;
