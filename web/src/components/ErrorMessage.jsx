/** The error state every page renders when a request fails. */
export function ErrorMessage({ title = 'Something went wrong', message }) {
  return (
    <div className="error-message" role="alert">
      <strong className="error-message__title">{title}</strong>
      {message ? <p className="error-message__body">{message}</p> : null}
    </div>
  );
}

export default ErrorMessage;
