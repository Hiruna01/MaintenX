/**
 * The overdue marker. It shows the API's `isOverdue` and nothing else — which deadline was
 * missed, and whether it has passed, is decided in VerificationService on the sweep's own
 * clocks. The title only says what each state is waiting on.
 */
export function OverdueFlag({ status }) {
  const waitingOn =
    status === 'Pending'
      ? 'Due, and the sweep has not asked the reporter yet.'
      : 'The reporter has not answered within the response window.';

  return (
    <span className="overdue-flag" title={waitingOn}>
      Overdue
    </span>
  );
}

export default OverdueFlag;
