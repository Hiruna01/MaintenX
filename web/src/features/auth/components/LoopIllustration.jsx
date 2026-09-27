/**
 * The MaintenX loop as a line drawing: a QR sticker, a dotted path through diagnosis and
 * repair to a verified check, and the arc back for a repair that did not hold. Decorative —
 * the headline beside it says the same thing in words.
 */
export function LoopIllustration() {
  return (
    <svg viewBox="0 0 420 170" fill="none" aria-hidden="true" focusable="false">
      {/* The return arc: a repair that did not hold goes back to diagnosis. */}
      <path d="M362 58 C 340 8, 190 8, 168 58" stroke="#d6d9df" strokeWidth="1.2" strokeDasharray="2 5" strokeLinecap="round" />
      <path d="M173 50 l-5 8 9 -1" stroke="#d6d9df" strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round" />

      {/* The main path. */}
      <path d="M72 96 H 348" stroke="#c9cdd4" strokeWidth="1.3" strokeDasharray="1.5 6" strokeLinecap="round" />

      {/* The sticker. */}
      <g transform="translate(28 66)">
        <rect x="0.5" y="0.5" width="60" height="60" rx="14" fill="#ffffff" stroke="#15171c" strokeWidth="1.3" />
        <rect x="12" y="12" width="13" height="13" rx="3" stroke="#15171c" strokeWidth="1.6" />
        <rect x="36" y="12" width="13" height="13" rx="3" stroke="#15171c" strokeWidth="1.6" />
        <rect x="12" y="36" width="13" height="13" rx="3" stroke="#15171c" strokeWidth="1.6" />
        <rect x="38" y="38" width="9" height="9" rx="2" fill="#15171c" />
      </g>

      {/* Diagnose — a ring of thought, drawn light. */}
      <g transform="translate(168 96)">
        <circle r="24" fill="#ffffff" stroke="#b9bdc6" strokeWidth="1.2" />
        <circle r="17" stroke="#e3e6eb" strokeWidth="1.2" />
        <path d="M-6 -2 a6 6 0 1 1 6 6 v4" stroke="#5b50e8" strokeWidth="1.6" strokeLinecap="round" />
        <circle cx="0" cy="13" r="1.3" fill="#5b50e8" />
      </g>

      {/* Repair. */}
      <g transform="translate(262 96)">
        <circle r="24" fill="#ffffff" stroke="#b9bdc6" strokeWidth="1.2" />
        <path d="M-7 7 L4 -4 M1 -9 a6 6 0 0 0 8 8 l-3 3 -8 -8 z" stroke="#15171c" strokeWidth="1.5" strokeLinejoin="round" strokeLinecap="round" />
      </g>

      {/* Verified. */}
      <g transform="translate(362 96)">
        <circle r="30" fill="#ffffff" stroke="#15171c" strokeWidth="1.3" />
        <circle r="24" fill="#eaf7f0" stroke="#cdebd9" strokeWidth="1" />
        <path d="M-9 0 l6 6 12 -13" stroke="#0e8a4f" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" />
      </g>

      <g fontFamily="JetBrains Mono, ui-monospace, monospace" fontSize="9.5" fill="#9aa0aa" textAnchor="middle" letterSpacing="0.6">
        <text x="58" y="148">REPORT</text>
        <text x="168" y="148">DIAGNOSE</text>
        <text x="262" y="148">REPAIR</text>
        <text x="362" y="148">VERIFY</text>
      </g>
    </svg>
  );
}

export default LoopIllustration;
