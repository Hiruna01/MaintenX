import { useState } from 'react';

import Button from '../../../components/Button';
import { assignTechnician } from '../services/workOrdersApi';
import TechnicianSelect from './TechnicianSelect';

/**
 * Hands the order to a technician — or to a different one. FacilitiesManager only, and only
 * rendered for one. It books no time: choosing when is the slot finder's job, and the API
 * keeps the two apart for the same reason (see AssignTechnicianDto).
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
    <form className="inline-form" onSubmit={handleSubmit} noValidate>
      <label htmlFor="assign-technician">{current ? 'Reassign to' : 'Assign to'}</label>
      <div className="inline-form__row">
        <TechnicianSelect
          id="assign-technician"
          value={technicianId}
          onChange={setTechnicianId}
          emptyLabel="Choose a technician…"
          disabled={isSubmitting}
          invalid={Boolean(error)}
        />
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Assigning…' : current ? 'Reassign' : 'Assign'}
        </Button>
      </div>
      {error ? (
        <p className="form__error" role="alert">
          {error}
        </p>
      ) : null}
    </form>
  );
}

export default AssignTechnicianForm;
