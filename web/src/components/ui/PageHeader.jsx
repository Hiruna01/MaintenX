import { ChevronRight } from 'lucide-react';
import { Link } from 'react-router-dom';

import styles from './ui.module.css';

/**
 * Breadcrumbs, a large light title, a lead line and the page's actions.
 * `crumbs` is [{ label, to? }]; the last one is the current page.
 */
export function PageHeader({ crumbs, title, lead, actions, children }) {
  return (
    <header className={styles.pageHeader}>
      <div>
        {crumbs?.length ? (
          <nav aria-label="Breadcrumb">
            <ol className={styles.crumbs}>
              {crumbs.map((crumb, index) => {
                const isLast = index === crumbs.length - 1;
                return (
                  <li key={`${crumb.label}-${index}`} style={{ display: 'contents' }}>
                    {crumb.to && !isLast ? (
                      <Link to={crumb.to}>{crumb.label}</Link>
                    ) : (
                      <span className={isLast ? styles.crumbCurrent : undefined} aria-current={isLast ? 'page' : undefined}>
                        {crumb.label}
                      </span>
                    )}
                    {isLast ? null : <ChevronRight aria-hidden="true" />}
                  </li>
                );
              })}
            </ol>
          </nav>
        ) : null}
        <h1 className={styles.title}>{title}</h1>
        {lead ? <p className={styles.lead}>{lead}</p> : null}
        {children}
      </div>
      {actions ? <div className={styles.headerActions}>{actions}</div> : null}
    </header>
  );
}

export default PageHeader;
