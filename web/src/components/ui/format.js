/** Display-only time helpers for the redesigned screens. Nothing here decides a rule. */

const RELATIVE = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

/** "3 days ago", "in 2 hours" — for an instant (an ISO string with a zone). */
export function timeAgo(iso) {
  if (!iso) return '—';
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '—';

  const seconds = Math.round((then - Date.now()) / 1000);
  const units = [
    ['year', 31536000],
    ['month', 2592000],
    ['week', 604800],
    ['day', 86400],
    ['hour', 3600],
    ['minute', 60],
  ];
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return RELATIVE.format(Math.round(seconds / size), unit);
  }
  return 'just now';
}

export function greeting(date = new Date()) {
  const hour = date.getHours();
  if (hour < 12) return 'Good morning';
  if (hour < 18) return 'Good afternoon';
  return 'Good evening';
}

export function firstName(fullName) {
  return String(fullName ?? '').trim().split(/\s+/)[0] || 'there';
}

export function initials(fullName) {
  return String(fullName ?? '')
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('');
}

/** "41 s", "3 min", "2 h 5 min" — how long something took, for display. */
export function formatDuration(ms) {
  if (ms === null || ms === undefined || Number.isNaN(ms) || ms < 0) return '—';
  const seconds = Math.round(ms / 1000);
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours < 48) return rest ? `${hours} h ${rest} min` : `${hours} h`;
  return `${Math.round(hours / 24)} days`;
}

/** An instant in the reader's local time: "26 Sep, 14:05". */
export function formatInstant(iso) {
  if (!iso) return '—';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';
  return date.toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
}
