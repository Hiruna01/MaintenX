import styles from './ui.module.css';

/** A shimmering placeholder block, sized by the caller so it matches the content it stands in for. */
export function Skeleton({ width = '100%', height = 14, radius, style }) {
  return (
    <span
      className={styles.skeleton}
      style={{ width, height, borderRadius: radius, ...style }}
      aria-hidden="true"
    />
  );
}

export default Skeleton;
