import clsx from 'clsx';
import { forwardRef } from 'react';
import { Link } from 'react-router-dom';

import styles from './ui.module.css';

const VARIANTS = {
  primary: styles.btnPrimary,
  secondary: styles.btnSecondary,
  ghost: styles.btnGhost,
  danger: styles.btnDanger,
};

/**
 * The redesigned button: a pill. `to` renders a router Link, so a navigation still reads as
 * a link to a screen reader and still opens in a new tab on Cmd/Ctrl-click.
 */
export const MxButton = forwardRef(function MxButton(
  { variant = 'secondary', size, icon: Icon, iconOnly = false, to, className, children, type = 'button', ...rest },
  ref,
) {
  const classes = clsx(
    styles.btn,
    VARIANTS[variant],
    size === 'sm' && styles.btnSm,
    iconOnly && styles.btnIcon,
    className,
  );
  const content = (
    <>
      {Icon ? <Icon aria-hidden="true" strokeWidth={1.8} /> : null}
      {children}
    </>
  );

  if (to) {
    return (
      <Link ref={ref} to={to} className={classes} {...rest}>
        {content}
      </Link>
    );
  }

  return (
    <button ref={ref} type={type} className={classes} {...rest}>
      {content}
    </button>
  );
});

export default MxButton;
