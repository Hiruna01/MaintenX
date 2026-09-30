import { useEffect, useRef, useState } from 'react';

import MxButton from '../../../components/ui/Button';
import SlideOver from '../../../components/ui/SlideOver';
import styles from '../users.module.css';

/**
 * The slide-over both account panels sit in, with the same "discard unsaved changes?" check
 * as the asset forms: a close the user starts (Esc, the overlay, ✕, Cancel) asks first while
 * the form is dirty.
 *
 * Unlike AssetFormSheet it leaves loading and error to its children, because the manage
 * panel's body remounts — and refetches — after every change while the sheet itself stays open.
 *
 * `children({ onDirtyChange, onCancel })` renders the content.
 */
export function UserSheet({ panel, title, description, children }) {
  const [dirty, setDirty] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const dirtyRef = useRef(false);

  // The close guard runs outside render and needs the latest value, so it reads a ref.
  useEffect(() => {
    dirtyRef.current = dirty;
  }, [dirty]);

  const { setGuard } = panel;
  useEffect(() => {
    setGuard(() => {
      if (!dirtyRef.current) return true;
      setConfirming(true);
      return false;
    });
    return () => setGuard(null);
  }, [setGuard]);

  return (
    <SlideOver
      open={panel.open}
      onOpenChange={panel.onOpenChange}
      title={title}
      description={description}
      meta={
        confirming && dirty ? (
          <div className={styles.discardBar} role="alertdialog" aria-label="Discard unsaved changes?">
            <span>You have unsaved changes. Discard them?</span>
            <div>
              <MxButton size="sm" onClick={() => setConfirming(false)} autoFocus>
                Keep editing
              </MxButton>
              <MxButton size="sm" variant="primary" onClick={() => panel.closeThen()}>
                Discard
              </MxButton>
            </div>
          </div>
        ) : null
      }
    >
      {children({ onDirtyChange: setDirty, onCancel: panel.requestClose })}
    </SlideOver>
  );
}

export default UserSheet;
