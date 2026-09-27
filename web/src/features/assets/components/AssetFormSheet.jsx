import { useEffect, useRef, useState } from 'react';

import MxButton from '../../../components/ui/Button';
import Skeleton from '../../../components/ui/Skeleton';
import SlideOver from '../../../components/ui/SlideOver';
import { ErrorState } from '../../../components/ui/States';
import styles from '../assets.module.css';

/**
 * The slide-over both asset forms sit in: loading and error states for whatever the form
 * needs first, and the "discard unsaved changes?" check before any close the user starts.
 *
 * `renderForm({ onDirtyChange, onCancel })` renders the form once its data is in.
 */
export function AssetFormSheet({ panel, title, description, meta, isLoading, error, errorTitle, renderForm }) {
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
        <>
          {meta}
          {confirming && dirty ? (
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
          ) : null}
        </>
      }
    >
      {isLoading ? (
        <div className={styles.stack} role="status" aria-label="Loading">
          <Skeleton height={92} radius={14} />
          <Skeleton height={42} radius={10} />
          <Skeleton height={42} radius={10} />
          <Skeleton height={42} radius={10} />
          <Skeleton height={42} radius={10} />
        </div>
      ) : null}

      {!isLoading && error ? (
        <ErrorState
          title={errorTitle}
          message={error.message}
          action={<MxButton onClick={() => panel.closeThen()}>Close</MxButton>}
        />
      ) : null}

      {!isLoading && !error ? renderForm({ onDirtyChange: setDirty, onCancel: panel.requestClose }) : null}
    </SlideOver>
  );
}

export default AssetFormSheet;
