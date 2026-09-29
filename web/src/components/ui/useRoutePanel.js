import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';

// Long enough for the sheet's slide-out to finish before the route that renders it goes away.
const EXIT_MS = 230;

/**
 * State for a slide-over that IS a route (/assets/new, /assets/:id/edit).
 *
 * Opening is arriving at the URL. Closing plays the exit animation first and then navigates
 * to `backTo`, so the panel slides out instead of vanishing. `guard` is asked before a close
 * the user started (Esc, the overlay, ✕, Cancel); returning false keeps the panel open —
 * that is how "discard unsaved changes?" gets its say.
 */
export function useRoutePanel(backTo) {
  const navigate = useNavigate();
  const [open, setOpen] = useState(true);
  const timer = useRef(null);
  const guardRef = useRef(null);

  useEffect(() => () => clearTimeout(timer.current), []);

  const closeThen = useCallback(
    (to = backTo, options) => {
      setOpen(false);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => navigate(to, options), EXIT_MS);
    },
    [backTo, navigate],
  );

  const requestClose = useCallback(() => {
    if (guardRef.current && guardRef.current() === false) return;
    closeThen();
  }, [closeThen]);

  const setGuard = useCallback((guard) => {
    guardRef.current = guard;
  }, []);

  const onOpenChange = useCallback(
    (next) => {
      if (!next) requestClose();
    },
    [requestClose],
  );

  return { open, onOpenChange, requestClose, closeThen, setGuard };
}

export default useRoutePanel;
