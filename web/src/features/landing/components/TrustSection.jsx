import { Pill } from '../../../components/ui/Pill';
import { LANDING_FACTS } from '../services/landingFacts';
import styles from '../landing.module.css';

/**
 * "The AI advises. The rules decide." — the project's central rule, said to a visitor — and the
 * numbers row, which quotes rules rather than marketing figures (see landingFacts.js).
 */
export function TrustSection() {
  return (
    <>
      <section id="trust" className={`${styles.section} ${styles.centered}`} aria-labelledby="trust-title">
        <h2 id="trust-title" className={styles.h2Large}>
          <span className={styles.quiet}>The AI advises.</span>
          <span>The rules decide.</span>
        </h2>
        <p className={styles.centerLead}>
          Agents read the facts and write advice. Money, warranties, schedules and approvals are rules a person can read —
          never a prompt.
        </p>

        <ul className={styles.trust}>
          <li className={styles.trustCard}>
            <div className={styles.trustArt} aria-hidden="true">
              <span className={styles.pillRow}>
                <Pill tone="violet" dot={false}>
                  Advice
                </Pill>
                <Pill tone="slate">Low confidence</Pill>
                <Pill tone="slate" dot={false}>
                  Suggests: inspect
                </Pill>
              </span>
            </div>
            <div>
              <span className={`${styles.kicker} mx-mono`}>HONEST</span>
              <h3 className={styles.trustTitle}>Advice, labelled as advice</h3>
              <p className={styles.trustBody}>Every agent output says what it is and how sure it is.</p>
            </div>
          </li>
          <li className={styles.trustCard}>
            <div className={`${styles.codeBlock} mx-mono`} aria-hidden="true">
              <span>
                estimate &gt; Rs 15,000 <span className={styles.codeDim}>→ manager</span>
              </span>
              <span>
                warranty ≥ today <span className={styles.codeDim}>→ covered</span>
              </span>
              <span>
                3 visits in 90 days <span className={styles.codeDim}>→ repeat</span>
              </span>
            </div>
            <div>
              <span className={`${styles.kicker} mx-mono`}>DETERMINISTIC</span>
              <h3 className={styles.trustTitle}>Rules you can read</h3>
              <p className={styles.trustBody}>Same input, same answer — every time, for everyone.</p>
            </div>
          </li>
          <li className={styles.trustCard}>
            <div className={styles.auditRows} aria-hidden="true">
              <span>
                Clarifier · <span className="mx-mono">get_room</span>
                <strong className={styles.okText}>Ok</strong>
              </span>
              <span>
                Diagnostic · agent run
                <strong className={styles.okText}>Ok</strong>
              </span>
              <span>
                Unknown tool · <span className="mx-mono">approve</span>
                <strong className={styles.refusedText}>Refused</strong>
              </span>
            </div>
            <div>
              <span className={`${styles.kicker} mx-mono`}>AUDITABLE</span>
              <h3 className={styles.trustTitle}>Every step on the record</h3>
              <p className={styles.trustBody}>Refused calls included. Nothing happens off the books.</p>
            </div>
          </li>
        </ul>
      </section>

      <section className={styles.facts} aria-labelledby="facts-title">
        <h2 id="facts-title" className={styles.factsTitle}>
          Our numbers aren’t marketing. They’re the rules.
        </h2>
        <dl className={styles.factsGrid}>
          {LANDING_FACTS.map((fact) => (
            <div key={fact.value} className={styles.fact}>
              <dt className={`${styles.factValue} mx-mono`}>{fact.value}</dt>
              <dd className={styles.factLabel}>{fact.label}</dd>
            </div>
          ))}
        </dl>
      </section>
    </>
  );
}

export default TrustSection;
