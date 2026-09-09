import { useEffect, useState } from 'react';

/**
 * Returns `value` only after it has stopped changing for `delay` milliseconds.
 *
 * Every keystroke schedules a timer and the cleanup cancels the previous one, so a fast
 * typist produces exactly one update instead of one per character. Used by every search
 * input in the app.
 */
export function useDebounce(value, delay = 400) {
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const timerId = setTimeout(() => setDebouncedValue(value), delay);
    return () => clearTimeout(timerId);
  }, [value, delay]);

  return debouncedValue;
}

export default useDebounce;
