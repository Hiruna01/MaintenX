import { ArrowDown, ChartLine, Check, History, RotateCcw, Scale, ShieldCheck } from 'lucide-react';

import { Pill } from '../../../components/ui/Pill';
import styles from '../landing.module.css';

function Point({ icon: Icon, title, children }) {
  return (
    <li className={styles.point}>
      <span className={styles.pointIcon} aria-hidden="true">
        <Icon size={20} strokeWidth={1.8} />
      </span>
      <div>
        <p className={styles.pointTitle}>{title}</p>
        <p className={styles.pointBody}>{children}</p>
      </div>
    </li>
  );
}

/** One row: the words on one side, a drawn piece of the product on the other. */
function Row({ label, title, lead, points, mock, flip = false }) {
  return (
    <div className={`${styles.row} ${flip ? styles.rowFlip : ''}`}>
      <div className={styles.rowText}>
        <span className={styles.label}>{label}</span>
        <h3 className={styles.h3}>{title}</h3>
        <p className={styles.rowLead}>{lead}</p>
        <ul className={styles.points}>{points}</ul>
      </div>
      <div className={styles.rowMock} aria-hidden="true">
        {mock}
      </div>
    </div>
  );
}

/**
 * Three moments of the loop, closer up: the diagnosis, the approval gate, and verification.
 * The right-hand pictures are drawings of the real screens (the diagnosis panel, an approval
 * card, the three voices of a check) — decoration, so each row's words carry the meaning.
 */
export function FeatureRows() {
  return (
    <section className={styles.rows} aria-label="The loop, closer up">
      <Row
        label="Diagnostic agent"
        title="Diagnosed from evidence, not a guess."
        lead="Every cause it proposes cites the visits it came from. With no history, it says so — it never invents one."
        points={
          <>
            <Point icon={History} title="Reads the whole service history">
              Terse technician notes, read together, become a pattern.
            </Point>
            <Point icon={ShieldCheck} title="Labelled as advice, everywhere">
              A person reads it. Nothing acts on it by itself.
            </Point>
          </>
        }
        mock={
          <div className={styles.mockCard}>
            <div className={styles.floatHead}>
              <span className={styles.eyebrow}>Most likely cause</span>
              <Pill tone="violet" dot={false}>
                Advice
              </Pill>
            </div>
            <p className={styles.mockTitle}>Overheating and thermal shutdown from a failing cooling fan</p>
            <ul className={styles.evidence}>
              <li>Visit 1 — no fault found after a test run</li>
              <li>Visit 2 — running hot, filter cleaned, temporary fix</li>
              <li>Visit 3 — same fault, fan bearing weak, replacement recommended</li>
            </ul>
            <div className={styles.floatFoot}>
              <Pill tone="green">High confidence</Pill>
              <span className={styles.floatMeta}>Suggests</span>
              <Pill tone="red" dot={false}>
                Replace
              </Pill>
            </div>
          </div>
        }
      />

      <Row
        flip
        label="Approval gate"
        title="A person signs off on the big ones."
        lead="The agent proposes a plan and a cost. Whether it needs a manager is plain arithmetic in the system — the agent is never even told the threshold."
        points={
          <>
            <Point icon={Scale} title="Over the threshold, or a replacement">
              Goes to the approval queue with everything needed to decide.
            </Point>
            <Point icon={Check} title="Everything else, approved on the spot">
              No queue for a Rs 6,500 fix.
            </Point>
          </>
        }
        mock={
          <div className={styles.mockCard}>
            <div className={styles.floatHead}>
              <span className="mx-mono">PRJ-MAB101-01</span>
              <Pill tone="amber">Awaiting approval</Pill>
            </div>
            <p className={styles.mockTitle}>Rs 45,000 — above the Rs 15,000 approval threshold</p>
            <div className={styles.compare}>
              <span />
              <span>Agent proposed</span>
              <span>Order as raised</span>
              <span className={styles.compareKey}>Strategy</span>
              <span>Replace</span>
              <span>Replace</span>
              <span className={styles.compareKey}>Estimate</span>
              <span className="mx-mono">Rs 45,000</span>
              <span className="mx-mono">Rs 45,000</span>
            </div>
            <div className={styles.decisionBar}>
              <span className={styles.ghostChip}>Reject</span>
              <span className={styles.ghostChip}>Request revision</span>
              <span className={styles.inkChip}>Approve</span>
            </div>
          </div>
        }
      />

      <Row
        label="Verification"
        title="The reporter has the last word."
        lead="A completed job is the technician’s account. Five days later, the person who reported the fault says whether it’s really gone."
        points={
          <>
            <Point icon={RotateCcw} title="“Still broken” loops straight back">
              Diagnosed again, with this repair now on the record.
            </Point>
            <Point icon={ChartLine} title="A confirmation rate you can believe">
              Counted only from answers actually given.
            </Point>
          </>
        }
        mock={
          <div className={styles.voices}>
            <div className={styles.voice}>
              <span className={styles.voiceLabel}>1 · The technician said</span>
              <p>“Replaced fan, cleaned filter, ran 30 min — OK.”</p>
            </div>
            <ArrowDown size={18} strokeWidth={1.8} className={styles.voiceArrow} />
            <div className={styles.voice}>
              <span className={styles.voiceLabel}>2 · The reporter says</span>
              <p className={styles.voiceSplit}>
                No — it cut out again today.
                <Pill tone="red">Reopened</Pill>
              </p>
            </div>
            <ArrowDown size={18} strokeWidth={1.8} className={styles.voiceArrow} />
            <div className={`${styles.voice} ${styles.voiceDark}`}>
              <span className={styles.voiceLabel}>3 · Back to diagnosis</span>
              <p>A second opinion runs — the first stays beside it.</p>
            </div>
          </div>
        }
      />
    </section>
  );
}

export default FeatureRows;
