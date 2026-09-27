import { Shield, ShieldCheck, ShieldOff } from 'lucide-react';

import { Pill } from '../../../components/ui/Pill';
import { formatDateOnly } from '../services/assetsApi';

/**
 * Warranty as a pill. `isUnderWarranty` is the API's answer from the failure summary — it is
 * never recomputed by comparing the expiry date with today. A null expiry is "no warranty
 * recorded", the same grey as expired but a different fact, so it says so.
 */
export function WarrantyPill({ isUnderWarranty, warrantyExpiresOn, isLoading, hasError }) {
  if (isLoading) return <Pill tone="slate" icon={Shield}>Checking warranty…</Pill>;
  if (hasError || typeof isUnderWarranty !== 'boolean') {
    return <Pill tone="slate" icon={Shield}>Warranty unavailable</Pill>;
  }
  if (isUnderWarranty) {
    return (
      <Pill tone="green" icon={ShieldCheck}>
        Under warranty · {formatDateOnly(warrantyExpiresOn)}
      </Pill>
    );
  }
  return (
    <Pill tone="slate" icon={ShieldOff}>
      {warrantyExpiresOn ? `Warranty ended · ${formatDateOnly(warrantyExpiresOn)}` : 'No warranty recorded'}
    </Pill>
  );
}

export default WarrantyPill;
