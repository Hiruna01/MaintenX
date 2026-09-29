import { Pill } from '../../../components/ui/Pill';
import { strategyLabel } from '../services/workOrdersApi';

// Presentation only. A replacement is marked out because it always goes to a manager,
// whatever it costs — the rule itself is ApprovalBasisFor in C#.
const STRATEGY_TONES = {
  KnownFix: 'green',
  SingleJob: 'blue',
  ConsolidatedJob: 'violet',
  InspectFirst: 'slate',
  Defer: 'slate',
  EscalateReplacement: 'red',
};

/** How the work is to be approached, as a pill keyed by the `WorkOrderStrategy` NAME. */
export function StrategyPill({ strategy }) {
  if (!strategy) return <Pill tone="slate">Not recognised</Pill>;
  return (
    <Pill tone={STRATEGY_TONES[strategy] ?? 'slate'} dot={false}>
      {strategyLabel(strategy)}
    </Pill>
  );
}

export default StrategyPill;
