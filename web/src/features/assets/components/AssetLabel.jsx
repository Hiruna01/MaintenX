import clsx from 'clsx';
import { Lock, Scissors } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';

import BrandMark from '../../../components/shell/BrandMark';
import styles from './AssetLabel.module.css';

/**
 * The asset's QR sticker, drawn the way it is printed.
 *
 * The QR code encodes the asset tag and NOTHING else — no URL, no prefix — because that is
 * what the phone's scanner sends to GET /api/assets/by-tag/{assetTag}. A sticker printed from
 * here scans exactly like one the registry issued.
 *
 * `size="compact"` is the one-line version used at the top of a slide-over. With no tag yet
 * (a form still being filled in) it draws a dimmed placeholder instead of a code for "".
 */
export function AssetLabel({ tag, name, location, size = 'full', locked = false, printable = false }) {
  const hasTag = Boolean(tag && tag.trim());
  const compact = size === 'compact';

  return (
    <figure
      className={clsx(styles.label, compact && styles.compact, printable && 'mx-print-target')}
      aria-label={hasTag ? `Asset label for ${tag}` : 'Asset label preview'}
    >
      {compact ? null : (
        <header className={styles.head}>
          <span className={styles.brand}>
            <BrandMark />
            MaintenX
          </span>
          <span className={styles.kind}>Asset</span>
        </header>
      )}

      <div className={styles.body}>
        <div className={clsx(styles.code, !hasTag && styles.codeEmpty)}>
          {hasTag ? (
            <QRCodeSVG
              value={tag.trim()}
              size={compact ? 64 : 104}
              level="M"
              bgColor="transparent"
              fgColor="#15171c"
              marginSize={0}
              title={`QR code for ${tag.trim()}`}
            />
          ) : (
            <span className={styles.codePlaceholder} aria-hidden="true" />
          )}
        </div>

        <figcaption className={styles.text}>
          <span className={clsx(styles.tag, !hasTag && styles.tagEmpty)}>
            {hasTag ? tag.trim() : 'TAG-PENDING'}
            {locked ? <Lock className={styles.lock} aria-label="Tag cannot be changed" /> : null}
          </span>
          <span className={styles.name}>{name?.trim() || 'Unnamed asset'}</span>
          {location ? <span className={styles.location}>{location}</span> : null}
        </figcaption>
      </div>

      {compact ? null : (
        <footer className={styles.foot}>
          <Scissors aria-hidden="true" />
          <span>Scan to look up this machine or report a fault</span>
        </footer>
      )}
    </figure>
  );
}

export default AssetLabel;
