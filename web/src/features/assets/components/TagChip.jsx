import { QrCode } from 'lucide-react';

import styles from '../assets.module.css';

/** The asset tag as a small printed label: a QR glyph and the tag in mono. */
export function TagChip({ tag }) {
  return (
    <span className={styles.tagChip}>
      <QrCode aria-hidden="true" strokeWidth={1.8} />
      {tag}
    </span>
  );
}

export default TagChip;
