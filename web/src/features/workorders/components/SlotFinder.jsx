import { Search } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { addDaysToDateOnly, todayDateOnly } from '../services/workOrdersApi';
import { DURATION_MAX, DURATION_MIN, validateSlotSearch } from '../services/workOrderValidation';
import styles from '../workorders.module.css';
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
 * and offers no Book button: a booking is time in somebody's diary.
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
    <div className={styles.slotFinder}>
      <form className={styles.slotForm} onSubmit={handleSubmit} noValidate>
        <div className={form.field}>
          <label htmlFor="slot-duration" className={form.label}>
            Job length (min)
          </label>
          <input
            id="slot-duration"
            type="number"
            className={`${form.input} ${form.mono}`}
            min={DURATION_MIN}
            max={DURATION_MAX}
            step="15"
            value={values.durationMinutes}
            onChange={(event) => handleChange('durationMinutes', event.target.value)}
            aria-invalid={errors.durationMinutes ? 'true' : undefined}
          />
          {errors.durationMinutes ? <p className={form.error}>{errors.durationMinutes}</p> : null}
        </div>

        <div className={form.field}>
          <label htmlFor="slot-from" className={form.label}>
            First day
          </label>
          <input
            id="slot-from"
            type="date"
            className={`${form.input} ${form.mono}`}
            value={values.fromDate}
            onChange={(event) => handleChange('fromDate', event.target.value)}
            aria-invalid={errors.fromDate ? 'true' : undefined}
          />
          {errors.fromDate ? <p className={form.error}>{errors.fromDate}</p> : null}
        </div>

        <div className={form.field}>
          <label htmlFor="slot-to" className={form.label}>
            Last day
          </label>
          <input
            id="slot-to"
            type="date"
            className={`${form.input} ${form.mono}`}
            value={values.toDate}
            min={values.fromDate || undefined}
            onChange={(event) => handleChange('toDate', event.target.value)}
            aria-invalid={errors.toDate ? 'true' : undefined}
          />
          {errors.toDate ? <p className={form.error}>{errors.toDate}</p> : null}
        </div>

        <MxButton type="submit" icon={Search} className={styles.slotSubmit}>
          Find free slots
        </MxButton>
      </form>

      <p className={styles.slotHint}>
        {technician
          ? `Checks the room's timetable and ${technician.fullName}'s other visits. `
          : 'Checks the room’s timetable only — assign a technician before booking. '}
        Campus-local dates, both included, up to 31 days. Times are shown in your local time.
      </p>

      {search ? (
        <SlotResults key={search.key} orderId={order.id} query={search.query} canBook={Boolean(technician)} onBooked={onBooked} />
      ) : null}
    </div>
  );
}

export default SlotFinder;
