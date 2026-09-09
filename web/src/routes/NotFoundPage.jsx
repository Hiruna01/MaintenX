import { Link } from 'react-router-dom';

/** The catch-all `*` route. */
export function NotFoundPage() {
  return (
    <section className="page page--narrow">
      <h1>Page not found</h1>
      <p className="page__lead">That address does not match any page in this app.</p>
      <Link to="/">Back to the start</Link>
    </section>
  );
}

export default NotFoundPage;
