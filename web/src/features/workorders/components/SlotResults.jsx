import { useState } from 'react';

import Button from '../../../components/Button';
import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';
import useAvailableSlots from '../hooks/useAvailableSlots';
import { formatSlot, scheduleWorkOrder } from '../services/workOrdersApi';

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
        message:
          err.status === 409
            ? `${err.message} Search again for a current list.`
            : err.message,
      });
      setBookingKey(null);
    }
  }

  if (isLoading) return <Spinner label="Finding free slots…" />;

  if (error) {
    // A 400 here is the API naming what is wrong with the search — say it as it said it.
    return <ErrorMessage title="Could not search for slots" message={error.message} />;
  }

  if (data.length === 0) {
    return (
      <div className="empty-state">
        <p className="empty-state__title">Nothing free in that range</p>
        <p className="empty-state__body">
          Every block of that length clashes with a class in the room
          {query.technicianId ? ' or another visit for the technician' : ''}, or falls outside
          working hours. Try a shorter job or a wider range.
        </p>
      </div>
    );
  }

  return (
    <ol className="slot-list" aria-label="Available slots">
      {data.map((slot) => (
        <li key={slot.startsAt} className="slot-list__item">
          <span className="slot-list__time">{formatSlot(slot)}</span>
          {canBook ? (
            <Button
              variant="secondary"
              onClick={() => book(slot)}
              disabled={bookingKey !== null}
            >
              {bookingKey === slot.startsAt ? 'Booking…' : 'Book'}
            </Button>
          ) : null}
          {bookError?.key === slot.startsAt ? (
            <p className="form__error slot-list__error" role="alert">
              {bookError.message}
            </p>
          ) : null}
        </li>
      ))}
    </ol>
  );
}

export default SlotResults;
