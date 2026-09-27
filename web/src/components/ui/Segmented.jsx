import clsx from 'clsx';

import styles from './ui.module.css';

/**
 * Segmented tabs — a radio group, so arrow keys and screen readers treat it as one choice.
 * `options` is [{ value, label, count?, icon? }].
 */
export function Segmented({ label, value, options, onChange, className }) {
  function handleKeyDown(event) {
    const index = options.findIndex((option) => option.value === value);
    let next = null;
    if (index === -1 && ['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp'].includes(event.key)) next = 0;
    else if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = (index + 1) % options.length;
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = (index - 1 + options.length) % options.length;
    if (next === null) return;
    event.preventDefault();
    onChange(options[next].value);
    event.currentTarget.querySelectorAll('[role="radio"]')[next]?.focus();
  }

  // With nothing chosen yet (a required choice with no default), the first option takes the
  // tab stop so the group can still be reached from the keyboard.
  const hasActive = options.some((option) => option.value === value);

  return (
    <div role="radiogroup" aria-label={label} className={clsx(styles.segmented, className)} onKeyDown={handleKeyDown}>
      {options.map((option, index) => {
        const isActive = option.value === value;
        const Icon = option.icon;
        return (
          <button
            key={option.value || 'all'}
            type="button"
            role="radio"
            aria-checked={isActive}
            tabIndex={isActive || (!hasActive && index === 0) ? 0 : -1}
            className={clsx(styles.segment, isActive && styles.segmentActive)}
            onClick={() => onChange(option.value)}
          >
            {Icon ? <Icon aria-hidden="true" strokeWidth={1.8} /> : null}
            {option.label}
            {option.count !== undefined ? <span className={styles.segmentCount}>{option.count ?? '·'}</span> : null}
          </button>
        );
      })}
    </div>
  );
}

export default Segmented;
