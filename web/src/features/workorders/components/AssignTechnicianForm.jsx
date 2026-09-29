import { UserRoundCheck } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { assignTechnician } from '../services/workOrdersApi';
import styles from '../workorders.module.css';
import TechnicianSelect from './TechnicianSelect';

/**
 * Hands the order to a technician — or to a different one. FacilitiesManager only, and only
 * rendered for one. It books no time: choosing when is the slot finder's job.
 *
 * Whether the chosen user really is a Technician, and whether the order can be assigned from
 * where it is, are the API's checks — a 400 or 409 comes back here as its own message.
 */
export function AssignTechnicianForm({ order, onAssigned }) {
  const current = order.assignedTechnician?.id ? String(order.assignedTechnician.id) : '';
  const [technicianId, setTechnicianId] = useState(current);
  const [error, setError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();

    // validate(): one field, and it must be a choice that changes something.
    if (!technicianId) {
      setError('Choose a technician.');
      return;
    }
    if (technicianId === current) {
      setError('That technician is already assigned.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await assignTechnician(order.id, technicianId);
      onAssigned(current ? 'Reassigned.' : 'Technician assigned. Now find a time and book the visit.');
    } catch (err) {
      setError(err.message);
      setIsSubmitting(false);
    }
  }

  return (
    <form className={styles.inlineForm} onSubmit={handleSubmit} noValidate>
      <div className={form.field} style={{ flex: 1 }}>
        <label htmlFor="assign-technician" className={form.label}>
          {current ? 'Reassign to' : 'Assign to'}
        </label>
        <TechnicianSelect
          id="assign-technician"
          block
          value={technicianId}
          onChange={(value) => {
            setTechnicianId(value);
            setError(null);
          }}
          placeholder="Choose a technician…"
          disabled={isSubmitting}
          invalid={Boolean(error)}
          describedBy={error ? 'assign-technician-error' : undefined}
        />
      </div>
      <MxButton type="submit" variant="primary" icon={UserRoundCheck} disabled={isSubmitting}>
        {isSubmitting ? 'Assigning…' : current ? 'Reassign' : 'Assign'}
      </MxButton>
      {error ? (
        <p className={form.error} id="assign-technician-error" role="alert" style={{ flexBasis: '100%' }}>
          {error}
        </p>
      ) : null}
    </form>
  );
}

export default AssignTechnicianForm;
