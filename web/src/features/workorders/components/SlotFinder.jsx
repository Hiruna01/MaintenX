import { useState } from 'react';

import Button from '../../../components/Button';
import { addDaysToDateOnly, todayDateOnly } from '../services/workOrdersApi';
import { DURATION_MAX, DURATION_MIN, validateSlotSearch } from '../services/workOrderValidation';
import SlotResults from './SlotResults';

/**
 * Asks the API's slot finder for free time to do this job, and books one of the answers.
 *
 * Nothing about availability is worked out here. Working hours, the class buffer either side
 * of every lecture in the room, the technician's other visits and the overlap test are all
 * SlotRules, in C#; this form only says how long and which days.
 *
 * With a technician assigned, the search asks for times THEY are free too — the same
 * technician the booking will be re-checked against. Without one it checks the room only,
 * which is what a manager wants before choosing who to send, and offers no Book button:
 * a booking is time in somebody's diary.
 */
export function SlotFinder({ order, onBooked }) {
  const technician = order.assignedTechnician;
  const [values, setValues] = useState(() => {
    const today = todayDateOnly();
    return { durationMinutes: '60', fromDate: today, toDate: addDaysToDateOnly(today, 6) };
  });
  const [errors, setErrors] = useState({});
  const [search, setSearch] = useState(null);

  function handleChange(name, value) {
    setValues((current) => ({ ...current, [name]: value }));
  }

  function handleSubmit(event) {
    event.preventDefault();

    const validation = validateSlotSearch(values);
    setErrors(validation);
    if (Object.keys(validation).length > 0) return;

    setSearch((previous) => ({
      // A new key remounts the results: a fresh mount is a fresh request.
      key: (previous?.key ?? 0) + 1,
      query: {
        assetId: order.asset.id,
        technicianId: technician?.id,
        durationMinutes: Number(values.durationMinutes),
        fromDate: values.fromDate,
        toDate: values.toDate,
      },
    }));
  }

  return (
    <div className="slot-finder">
      <form className="slot-finder__form" onSubmit={handleSubmit} noValidate>
        <label className="slot-finder__field">
          <span>Job length (minutes)</span>
          <input
            type="number"
            min={DURATION_MIN}
            max={DURATION_MAX}
            step="15"
            value={values.durationMinutes}
            onChange={(event) => handleChange('durationMinutes', event.target.value)}
            aria-invalid={errors.durationMinutes ? 'true' : undefined}
          />
          {errors.durationMinutes ? <span className="form__error">{errors.durationMinutes}</span> : null}
        </label>

        <label className="slot-finder__field">
          <span>First day</span>
          <input
            type="date"
            value={values.fromDate}
            onChange={(event) => handleChange('fromDate', event.target.value)}
            aria-invalid={errors.fromDate ? 'true' : undefined}
          />
          {errors.fromDate ? <span className="form__error">{errors.fromDate}</span> : null}
        </label>

        <label className="slot-finder__field">
          <span>Last day</span>
          <input
            type="date"
            value={values.toDate}
            min={values.fromDate || undefined}
            onChange={(event) => handleChange('toDate', event.target.value)}
            aria-invalid={errors.toDate ? 'true' : undefined}
          />
          {errors.toDate ? <span className="form__error">{errors.toDate}</span> : null}
        </label>

        <Button type="submit">Find free slots</Button>
      </form>

      <p className="slot-finder__hint">
        {technician
          ? `Checks the room's timetable and ${technician.fullName}'s other visits. `
          : 'Checks the room’s timetable only — assign a technician before booking. '}
        Campus-local dates, both included, up to 31 days. Times are shown in your local time.
      </p>

      {search ? (
        <SlotResults
          key={search.key}
          orderId={order.id}
          query={search.query}
          canBook={Boolean(technician)}
          onBooked={onBooked}
        />
      ) : null}
    </div>
  );
}

export default SlotFinder;
