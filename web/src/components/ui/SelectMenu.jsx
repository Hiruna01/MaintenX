import * as Select from '@radix-ui/react-select';
import clsx from 'clsx';
import { Check, ChevronDown } from 'lucide-react';

import styles from './ui.module.css';

// Radix Select cannot hold an empty-string value, so "no filter" travels as this sentinel.
const NONE = '__none__';

/**
 * A styled picker over a fixed list. `options` is [{ value, label }]; `value` '' means the
 * placeholder / "all" choice, given by `emptyLabel`.
 *
 * `inlineLabel` renders "Category: All" inside a filter pill; `block` renders a full-width
 * form control.
 */
export function SelectMenu({
  id,
  value,
  onChange,
  options,
  emptyLabel,
  placeholder,
  inlineLabel,
  ariaLabel,
  block = false,
  disabled = false,
  invalid = false,
  describedBy,
}) {
  const hasEmpty = Boolean(emptyLabel);
  const current = value === '' || value === null || value === undefined ? (hasEmpty ? NONE : undefined) : String(value);

  return (
    <Select.Root
      value={current}
      onValueChange={(next) => onChange(next === NONE ? '' : next)}
      disabled={disabled}
    >
      <Select.Trigger
        id={id}
        className={clsx(styles.selectTrigger, block && styles.selectTriggerBlock)}
        aria-label={ariaLabel}
        aria-invalid={invalid ? 'true' : 'false'}
        aria-describedby={describedBy}
      >
        {inlineLabel ? <span className={styles.selectLabel}>{inlineLabel}:</span> : null}
        <span className={styles.selectValue}>
          <Select.Value placeholder={placeholder} />
        </span>
        <Select.Icon asChild>
          <ChevronDown className={styles.selectChevron} aria-hidden="true" />
        </Select.Icon>
      </Select.Trigger>

      <Select.Portal>
        <Select.Content className={styles.selectContent} position="popper" sideOffset={6} data-mx-portal="">
          <Select.Viewport className={styles.selectViewport}>
            {hasEmpty ? <Option value={NONE} label={emptyLabel} /> : null}
            {options.map((option) => (
              <Option key={option.value} value={String(option.value)} label={option.label} />
            ))}
          </Select.Viewport>
        </Select.Content>
      </Select.Portal>
    </Select.Root>
  );
}

function Option({ value, label }) {
  return (
    <Select.Item value={value} className={styles.selectItem}>
      <Select.ItemText>{label}</Select.ItemText>
      <Select.ItemIndicator>
        <Check aria-hidden="true" />
      </Select.ItemIndicator>
    </Select.Item>
  );
}

export default SelectMenu;
