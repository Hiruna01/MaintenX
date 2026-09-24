import { strategyLabel } from '../services/workOrdersApi';

/**
 * How the work is to be approached, as a pill keyed by the `WorkOrderStrategy` NAME. A
 * replacement is marked out because it always goes to a manager, whatever it costs.
 */
export function StrategyBadge({ strategy }) {
  if (!strategy) {
    return <span className="strategy strategy--unknown">Not recognised</span>;
  }

  return <span className={`strategy strategy--${strategy}`}>{strategyLabel(strategy)}</span>;
}

export default StrategyBadge;
