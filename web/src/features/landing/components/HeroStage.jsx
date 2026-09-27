import { ArrowLeft, Check } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';

import { Pill } from '../../../components/ui/Pill';
import { DEMO_ASSET_TAG } from '../services/landingFacts';
import styles from '../landing.module.css';

/**
 * The hero illustration: the phone's clarification form, with the loop's other moments floating
 * around it — the scanned sticker, the diagnosis, the approval and "Is it fixed?".
 *
 * It is a picture of the product, not the product: every control is drawn, none works, so the
 * whole stage is one image to a screen reader with a sentence saying what it shows.
 */
export function HeroStage() {
  return (
    <div
      className={styles.stage}
      role="img"
      aria-label="The MaintenX phone app asking two quick questions about a projector, surrounded by the scanned QR sticker, the agent's diagnosis, an approval above the cost threshold, and the reporter being asked whether the repair held."
    >
      <div className={styles.stageArcs} aria-hidden="true">
        <span className={styles.arcOuter} />
        <span className={styles.arcInner} />
        <span className={styles.arcFill} />
      </div>

      <div className={styles.phone} aria-hidden="true">
        <div className={styles.phoneScreen}>
          <div className={styles.phoneTop}>
            <ArrowLeft size={18} strokeWidth={1.8} />
            <span className={styles.waitingTag}>Waiting on you</span>
          </div>
          <div>
            <p className={styles.phoneTitle}>A few quick questions</p>
            <p className={styles.phoneLead}>Answer both, then submit once. No chat — just the form.</p>
          </div>
          <div className={styles.phoneQuestion}>
            <span className={`${styles.phoneCount} mx-mono`}>QUESTION 1 OF 2</span>
            <p className={styles.phoneAsk}>Does the projector power on at all?</p>
            <span className={`${styles.phoneOption} ${styles.phoneOptionOn}`}>
              No lights or power at all <Check size={14} strokeWidth={2} />
            </span>
            <span className={styles.phoneOption}>Powers on, but no image</span>
            <span className={styles.phoneOption}>Powers on, then cuts out</span>
          </div>
          <div className={styles.phoneQuestion}>
            <span className={`${styles.phoneCount} mx-mono`}>QUESTION 2 OF 2</span>
            <p className={styles.phoneAsk}>Any burning smell, smoke or sparking?</p>
            <span className={styles.phoneToggle}>
              <span>Yes</span>
              <span className={styles.phoneToggleOn}>No</span>
            </span>
          </div>
          <span className={styles.phoneSubmit}>Submit answers</span>
        </div>
      </div>

      <div className={`${styles.float} ${styles.floatSticker}`} aria-hidden="true">
        <div className={styles.stickerRow}>
          {/* The real payload, exactly as AssetLabel prints it: scanning this finds the seeded asset. */}
          <QRCodeSVG value={DEMO_ASSET_TAG} size={72} level="M" bgColor="transparent" fgColor="#15171c" marginSize={0} />
          <div>
            <span className={styles.floatMeta}>Scanned</span>
            <p className={styles.floatStrong}>Lecture Hall A projector</p>
          </div>
        </div>
        <span className={`${styles.tagChip} mx-mono`}>{DEMO_ASSET_TAG}</span>
      </div>

      <div className={`${styles.float} ${styles.floatDiagnosis}`} aria-hidden="true">
        <div className={styles.floatHead}>
          <span className={styles.eyebrow}>Diagnosis</span>
          <Pill tone="violet" dot={false}>
            Advice
          </Pill>
        </div>
        <p className={styles.floatTitle}>Overheating from a failing cooling fan</p>
        <div className={styles.floatFoot}>
          <Pill tone="green">High confidence</Pill>
          <span className={`${styles.floatMeta} mx-mono`}>3 visits on record</span>
        </div>
      </div>

      <div className={`${styles.float} ${styles.floatApproval}`} aria-hidden="true">
        <div className={styles.floatHead}>
          <Pill tone="red" dot={false}>
            Replace the equipment
          </Pill>
          <Pill tone="amber">Awaiting approval</Pill>
        </div>
        <p className={`${styles.floatMoney} mx-mono`}>Rs 45,000</p>
        <p className={styles.floatBody}>Above the Rs 15,000 approval threshold — a manager decides.</p>
      </div>

      <div className={`${styles.float} ${styles.floatVerify}`} aria-hidden="true">
        <span className={styles.waitingTag}>Waiting on you</span>
        <p className={styles.floatAsk}>Is it fixed?</p>
        <p className={styles.floatMeta}>Asked 5 days after the repair.</p>
        <span className={styles.verifyChoices}>
          <span className={styles.verifyYes}>Yes, fixed</span>
          <span className={styles.verifyNo}>Still broken</span>
        </span>
      </div>
    </div>
  );
}

export default HeroStage;
