import { useState } from 'react';

/**
 * The reporter's photo of the fault, if they attached one.
 *
 * `photoUrl` is the object's public URL in Supabase Storage — the API stores the URL, never
 * the bytes — so the browser loads it directly. A URL that no longer resolves is shown as
 * exactly that, with the link, rather than as a broken-image icon that says nothing.
 */
export function ReportPhoto({ url }) {
  const [failed, setFailed] = useState(false);

  if (!url) {
    return <div className="report-photo report-photo--none">No photo attached</div>;
  }

  if (failed) {
    return (
      <div className="report-photo report-photo--none">
        The photo could not be loaded.{' '}
        <a href={url} target="_blank" rel="noreferrer">
          Open the link
        </a>
      </div>
    );
  }

  return (
    <a className="report-photo" href={url} target="_blank" rel="noreferrer" title="Open full size">
      <img src={url} alt="Photo of the reported fault" onError={() => setFailed(true)} />
    </a>
  );
}

export default ReportPhoto;
