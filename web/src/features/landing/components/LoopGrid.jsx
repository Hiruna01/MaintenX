import { ScanLine } from 'lucide-react';

import { DEMO_ASSET_TAG } from '../services/landingFacts';
import styles from '../landing.module.css';

/** One step of the loop: its number, a drawn detail, and a two-tone line. */
function Step({ number, name, tone, detail, lead, rest, note }) {
  return (
    <li className={`${styles.step} ${tone ? styles[`step-${tone}`] : ''}`}>
      <div className={styles.stepTop}>
        <div className={styles.stepDetail} aria-hidden="true">
          {detail}
        </div>
        <span className={`${styles.stepNumber} mx-mono`}>
          {number} · {name}
        </span>
      </div>
      <div>
        <h3 className={styles.stepTitle}>
          {lead} <span className={styles.stepRest}>{rest}</span>
        </h3>
        {note ? <p className={styles.stepNote}>{note}</p> : null}
      </div>
    </li>
  );
}

/**
 * The loop, as six cards: report, clarify, diagnose, decide, schedule, verify. The drawings are
 * decoration (aria-hidden); each card's heading carries what it means.
 */
export function LoopGrid() {
  return (
    <section id="loop" className={styles.section} aria-labelledby="loop-title">
      <div className={styles.sectionHead}>
        <div>
          <span className={styles.label}>How it works</span>
          <h2 id="loop-title" className={styles.h2}>
            One report. Six steps.
            <br />
            Nothing forgotten.
          </h2>
        </div>
        <p className={styles.sectionLead}>
          Every fault walks the same loop — and the loop only closes when the person who reported it says it’s fixed.
        </p>
      </div>

      <ol className={styles.loop}>
        <Step
          number="01"
          name="Report"
          tone="dark"
          detail={<ScanLine size={34} strokeWidth={1.6} />}
          lead="Scan the sticker."
          rest="We know exactly which machine it is."
          note={
            <span className={styles.stepChips}>
              <span className={`${styles.darkChip} mx-mono`}>{DEMO_ASSET_TAG}</span>
              <span className={styles.darkChip}>+ room filled in</span>
            </span>
          }
        />
        <Step
          number="02"
          name="Clarify"
          detail={
            <div className={styles.miniForm}>
              <span className={styles.miniToggle}>
                <span className={styles.miniToggleOn}>Yes</span>
                <span>No</span>
              </span>
              <span className={styles.miniChips}>
                <span>Every few minutes</span>
                <span>Once a lecture</span>
              </span>
            </div>
          }
          lead="Only the questions"
          rest="that change what the technician does."
          note="At most two. Often none. Never a chat."
        />
        <Step
          number="03"
          name="Diagnose"
          detail={
            <ul className={styles.miniVisits}>
              <li>
                <span className={styles.visitDot} /> Visit 1 — no fault found
              </li>
              <li>
                <span className={`${styles.visitDot} ${styles.visitAmber}`} /> Visit 2 — temporary fix
              </li>
              <li>
                <span className={`${styles.visitDot} ${styles.visitRed}`} /> Visit 3 — same fault again
              </li>
            </ul>
          }
          lead="Reads the machine’s own history."
          rest="Three visits is a pattern."
        />
        <Step
          number="04"
          name="Decide"
          detail={
            <div className={styles.miniGate}>
              <span className={styles.gateRow}>
                <span className="mx-mono">Rs 45,000</span>
                <span className={styles.gateAmber}>Manager decides</span>
              </span>
              <span className={styles.gateBar}>
                <span className={`${styles.gateFill} ${styles.gateFillHigh}`} />
                <span className={styles.gateLine} />
              </span>
              <span className={styles.gateRow}>
                <span className="mx-mono">Rs 6,500</span>
                <span className={styles.gateGreen}>Approved at once</span>
              </span>
              <span className={styles.gateBar}>
                <span className={`${styles.gateFill} ${styles.gateFillLow}`} />
                <span className={styles.gateLine} />
              </span>
            </div>
          }
          lead="A person signs off"
          rest="on the big ones — and only those."
        />
        <Step
          number="05"
          name="Schedule"
          detail={
            <div className={styles.miniDay}>
              <span className={styles.dayStrip}>
                <span />
                <span className={styles.dayBuffer} />
                <span className={styles.dayClass} />
                <span className={styles.dayBuffer} />
                <span />
                <span className={styles.dayFree} />
                <span />
                <span className={styles.dayClass} />
                <span />
              </span>
              <span className={`${styles.dayScale} mx-mono`}>
                <span>08:00</span>
                <span>13:00 free</span>
                <span>17:00</span>
              </span>
            </div>
          }
          lead="Booked around lectures,"
          rest="never into one."
          note="Read from the campus timetable, with a buffer either side."
        />
        <Step
          number="06"
          name="Verify"
          tone="iris"
          detail={
            <span className={styles.miniDays}>
              <span className={`${styles.dayNode} mx-mono`}>0</span>
              <span className={styles.dayLink} />
              <span className={styles.dayPip} />
              <span className={styles.dayPip} />
              <span className={styles.dayPip} />
              <span className={styles.dayPip} />
              <span className={styles.dayLink} />
              <span className={`${styles.dayNode} ${styles.dayNodeIris} mx-mono`}>5</span>
            </span>
          }
          lead="Five days later,"
          rest="we ask if it held."
          note="Asked the same afternoon, everyone says yes. So we wait."
        />
      </ol>
    </section>
  );
}

export default LoopGrid;
