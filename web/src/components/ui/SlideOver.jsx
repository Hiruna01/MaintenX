import * as Dialog from '@radix-ui/react-dialog';
import clsx from 'clsx';
import { Maximize2, Minimize2, SquareArrowOutUpRight, X } from 'lucide-react';
import { useState } from 'react';

import MxButton from './Button';
import styles from './ui.module.css';

/**
 * A panel that slides in from the right over a dimmed, blurred page.
 *
 * Controlled: the caller owns `open` and decides what closing means (a route panel navigates
 * back once the exit animation has played). Radix supplies the focus trap, Esc, the overlay
 * click and returning focus to whatever opened it.
 *
 * `openFullTo` adds an "Open full" link; `expandable` adds a button that widens the sheet.
 */
export function SlideOver({
  open,
  onOpenChange,
  title,
  description,
  meta,
  footer,
  openFullTo,
  expandable = true,
  children,
}) {
  const [wide, setWide] = useState(false);

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className={styles.overlay} data-mx-portal="" />
        <Dialog.Content
          className={clsx(styles.sheet, wide && styles.sheetWide)}
          data-mx-portal=""
          /* Radix warns about a missing description unless told there is none on purpose. */
          {...(description ? {} : { 'aria-describedby': undefined })}
        >
          <div className={styles.sheetBar}>
            <div className={styles.sheetBarActions}>
              {openFullTo ? (
                <MxButton size="sm" icon={SquareArrowOutUpRight} to={openFullTo}>
                  Open full
                </MxButton>
              ) : null}
              {expandable ? (
                <MxButton
                  size="sm"
                  icon={wide ? Minimize2 : Maximize2}
                  onClick={() => setWide((current) => !current)}
                  aria-pressed={wide}
                >
                  {wide ? 'Collapse' : 'Expand'}
                </MxButton>
              ) : null}
            </div>
            <Dialog.Close asChild>
              <MxButton variant="ghost" size="sm" iconOnly icon={X} aria-label="Close" />
            </Dialog.Close>
          </div>

          <div className={styles.sheetHead}>
            <Dialog.Title className={styles.sheetTitle}>{title}</Dialog.Title>
            {description ? (
              <Dialog.Description className={styles.sheetDescription}>{description}</Dialog.Description>
            ) : null}
            {meta}
          </div>

          <div className={styles.sheetBody}>{children}</div>

          {footer ? <div className={styles.sheetFooter}>{footer}</div> : null}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export default SlideOver;
