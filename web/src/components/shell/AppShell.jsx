import clsx from 'clsx';
import { Menu, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';

import MxButton from '../ui/Button';
import BrandMark from './BrandMark';
import styles from './Shell.module.css';
import Sidebar from './Sidebar';

// The redesigned screens lay their own panels on the canvas; every other screen still sits on
// one white stage, so its existing styles read as they did under the old top bar.
const CANVAS_ROUTES = [/^\/dashboard\/?$/, /^\/assets(\/|$)/, /^\/reports(\/|$)/, /^\/workflows(\/|$)/, /^\/workorders(\/|$)/, /^\/approvals\/?$/, /^\/verifications(\/|$)/, /^\/metrics\/?$/];

/** The signed-in frame: sidebar on the left, the page on the right. */
export function AppShell({ children }) {
  const location = useLocation();
  const [navOpen, setNavOpen] = useState(false);
  const onCanvas = CANVAS_ROUTES.some((pattern) => pattern.test(location.pathname));

  // A drawer left open across a navigation would cover the page the user just asked for.
  useEffect(() => {
    // eslint-disable-next-line react/set-state-in-effect
    setNavOpen(false);
  }, [location.pathname]);

  return (
    <div className={clsx('mx-app', styles.shell)}>
      <div className={styles.topbar}>
        <Link to="/dashboard" className={styles.brand}>
          <span className={styles.brandMark}>
            <BrandMark />
          </span>
          <span className={styles.brandName}>MaintenX</span>
        </Link>
        <MxButton
          iconOnly
          icon={navOpen ? X : Menu}
          onClick={() => setNavOpen((current) => !current)}
          aria-label={navOpen ? 'Close navigation' : 'Open navigation'}
          aria-expanded={navOpen}
        />
      </div>

      <button
        type="button"
        className={clsx(styles.scrim, navOpen && styles.scrimOpen)}
        onClick={() => setNavOpen(false)}
        aria-hidden="true"
        tabIndex={-1}
      />

      <Sidebar open={navOpen} onNavigate={() => setNavOpen(false)} />

      <main className={styles.main}>
        <div className={clsx(styles.stage, onCanvas && styles.stageCanvas)}>
          <div className={styles.stageInner}>{children}</div>
        </div>
      </main>
    </div>
  );
}

export default AppShell;
