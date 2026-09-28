import { CalendarX2 } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useAvailableSlots from '../hooks/useAvailableSlots';
import { formatSlot, scheduleWorkOrder } from '../services/workOrdersApi';
import styles from '../workorders.module.css';

const DAY = { weekday: 'long', day: 'numeric', month: 'long' };
const TIME = { hour: '2-digit', minute: '2-digit' };

/** The offered slots grouped under the local day they start on — display grouping only. */
function groupByDay(slots) {
  const groups = [];
  for (const slot of slots) {
    const label = new Date(slot.startsAt).toLocaleDateString(undefined, DAY);
    const last = groups[groups.length - 1];
    if (last && last.label === label) last.slots.push(slot);
    else groups.push({ label, slots: [slot] });
  }
  return groups;
}

/**
 * The answer to ONE slot search, mounted fresh for each search so each is its own request.
 *
 * AN OFFER IS NOT A RESERVATION. Booking sends the slot back exactly as offered and the API
 * checks it again inside a serializable transaction: if somebody took it in the meantime the
 * answer is a 409, shown against that slot, and nothing is booked.
 */
export function SlotResults({ orderId, query, canBook, onBooked }) {
  const { data, isLoading, error } = useAvailableSlots(query);
  const [bookingKey, setBookingKey] = useState(null);
  const [bookError, setBookError] = useState(null);

  async function book(slot) {
    setBookingKey(slot.startsAt);
    setBookError(null);

    try {
      await scheduleWorkOrder(orderId, slot);
      onBooked(`Visit booked for ${formatSlot(slot)}.`);
    } catch (err) {
      setBookError({
        key: slot.startsAt,
        message: err.status === 409 ? `${err.message} Search again for a current list.` : err.message,
      });
      setBookingKey(null);
    }
  }

  if (isLoading) {
    return (
      <div className={styles.slotGrid} role="status" aria-label="Finding free slots">
        {Array.from({ length: 6 }, (_, index) => (
          <Skeleton key={index} height={52} radius={12} />
        ))}
      </div>
    );
  }

  // A 400 here is the API naming what is wrong with the search — say it as it said it.
  if (error) return <ErrorState compact title="Could not search for slots" message={error.message} />;

  if (data.length === 0) {
    return (
      <EmptyState
        compact
        icon={CalendarX2}
        title="Nothing free in that range"
        body={`Every block of that length clashes with a class in the room${
          query.technicianId ? ' or another visit for the technician' : ''
        }, or falls outside working hours. Try a shorter job or a wider range.`}
      />
    );
  }

  return (
    <div className={styles.slotDays}>
      <p className={styles.slotCount}>
        {data.length} free {data.length === 1 ? 'slot' : 'slots'}, earliest first
        {canBook ? '' : ' · assign a technician to book one'}
      </p>
      {groupByDay(data).map((group) => (
        <section key={group.label} aria-label={group.label}>
          <p className={styles.slotDay}>{group.label}</p>
          <ol className={styles.slotGrid}>
            {group.slots.map((slot) => (
              <li key={slot.startsAt} className={styles.slot}>
                <span className={styles.slotTime}>
                  {new Date(slot.startsAt).toLocaleTimeString(undefined, TIME)}
                  <span> – {new Date(slot.endsAt).toLocaleTimeString(undefined, TIME)}</span>
                </span>
                {canBook ? (
                  <MxButton size="sm" variant="primary" onClick={() => book(slot)} disabled={bookingKey !== null}>
                    {bookingKey === slot.startsAt ? 'Booking…' : 'Book'}
                  </MxButton>
                ) : null}
                {bookError?.key === slot.startsAt ? (
                  <p className={styles.slotError} role="alert">
                    {bookError.message}
                  </p>
                ) : null}
              </li>
            ))}
          </ol>
        </section>
      ))}
    </div>
  );
}

export default SlotResults;
