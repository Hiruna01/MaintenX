import { RefreshCcwDot } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import { runVerificationSweep } from '../services/verificationApi';

/**
 * Runs the verification sweep now instead of waiting for the API's timer — the same pass,
 * the same rules. Rendered for a FacilitiesManager only, the one role the endpoint admits.
 *
 * A demo cannot wait an hour for the timer, and a host that sleeps when idle may not have run
 * it at all. Pressing it twice is harmless: the second pass finds nothing left to move.
 */
export function RunSweepButton({ onSwept, onError }) {
  const [isRunning, setIsRunning] = useState(false);

  async function handleClick() {
    setIsRunning(true);
    try {
      onSwept(await runVerificationSweep());
    } catch (err) {
      onError(err.message);
    } finally {
      setIsRunning(false);
    }
  }

  return (
    <MxButton variant="primary" icon={RefreshCcwDot} onClick={handleClick} disabled={isRunning}>
      {isRunning ? 'Running sweep…' : 'Run sweep now'}
    </MxButton>
  );
}

export default RunSweepButton;
