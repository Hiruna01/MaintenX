import { Timer } from 'lucide-react';

import { formatInstant } from '../../../components/ui/format';
import { Pill } from '../../../components/ui/Pill';
import { humanize, toneFor } from '../../../components/ui/tones';

/**
 * The repair SLA as a pill: the API's `sla` verdict (a `SlaState` NAME) and its stamped
 * `dueAt`. Presentation only — whether an order is overdue is SlaRules in C#; nothing here
 * compares the due time with the browser's clock.
 *
 * `None` (no clock started — not approved yet, rejected, or from before the SLA) renders
 * nothing unless `showNone`, so the board is not filled with grey pills.
 */
export function SlaPill({ sla, dueAt, showNone = false }) {
  if (!sla || (sla === 'None' && !showNone)) return null;

  const due = formatInstant(dueAt);
  const labels = {
    None: 'No SLA clock yet',
    OnTrack: `On track · due ${due}`,
    Overdue: `Overdue · was due ${due}`,
    Met: 'SLA met',
    Missed: 'SLA missed',
  };

  return (
    <span title={dueAt ? `Repair SLA — due ${due}` : 'Repair SLA — starts when the order is approved'}>
      <Pill tone={toneFor(sla)} icon={Timer}>
        {labels[sla] ?? humanize(sla)}
      </Pill>
    </span>
  );
}

export default SlaPill;
