import { Archive, X } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { retireAsset } from '../services/assetsApi';

/**
 * Takes equipment out of service. Two steps — choose, then confirm — because it changes what
 * every screen says about the machine; it does not delete anything. The API sets the status to
 * Retired and keeps the row and its whole service history (DELETE /api/assets/{id} retires).
 *
 * Admin only, and only offered on an asset that is not retired already. The page reloads
 * through the same `state.refresh` the edit panel uses.
 */
export function RetireAssetButton({ asset }) {
  const navigate = useNavigate();
  const [confirming, setConfirming] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState(null);

  if (asset.status === 'Retired') return null;

  async function handleConfirm() {
    setIsSubmitting(true);
    setError(null);

    try {
      await retireAsset(asset.id);
      navigate(`/assets/${asset.id}`, { replace: true, state: { refresh: Date.now() } });
    } catch (caught) {
      setError(caught.message);
      setIsSubmitting(false);
    }
  }

  if (!confirming) {
    return (
      <MxButton icon={Archive} onClick={() => setConfirming(true)}>
        Retire
      </MxButton>
    );
  }

  return (
    <>
      <MxButton icon={X} onClick={() => setConfirming(false)} disabled={isSubmitting}>
        Keep in service
      </MxButton>
      <MxButton variant="danger" icon={Archive} onClick={handleConfirm} disabled={isSubmitting}>
        {isSubmitting ? 'Retiring…' : 'Confirm retire — history is kept'}
      </MxButton>
      {error ? (
        <p className={form.submitError} role="alert">
          {error}
        </p>
      ) : null}
    </>
  );
}

export default RetireAssetButton;
