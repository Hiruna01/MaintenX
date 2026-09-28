import { ImageOff, ImageIcon, SquareArrowOutUpRight } from 'lucide-react';
import { useState } from 'react';

import styles from '../reports.module.css';

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
    return (
      <div className={styles.photoNone}>
        <ImageIcon aria-hidden="true" />
        No photo attached
      </div>
    );
  }

  if (failed) {
    return (
      <div className={styles.photoNone}>
        <ImageOff aria-hidden="true" />
        The photo could not be loaded.
        <a href={url} target="_blank" rel="noreferrer">
          Open the link
        </a>
      </div>
    );
  }

  return (
    <a className={styles.photo} href={url} target="_blank" rel="noreferrer" title="Open full size">
      <img src={url} alt="Photo of the reported fault" onError={() => setFailed(true)} />
      <span className={styles.photoOpen}>
        <SquareArrowOutUpRight aria-hidden="true" /> Full size
      </span>
    </a>
  );
}

export default ReportPhoto;
