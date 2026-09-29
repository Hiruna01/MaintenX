import clsx from 'clsx';
import { Bot, CircleCheck, CircleX, Hourglass, MessageSquareQuote, Wrench } from 'lucide-react';

import { formatInstant } from '../../../components/ui/format';
import { AgentOutcomePill } from './VerificationPills';
import styles from '../verification.module.css';

/**
 * The check read as three voices, in the order they speak: the technician's claim, the
 * reporter's answer to it, and the agent's review of both. Numbered because it IS a sequence —
 * each one is recorded after the one before it.
 *
 * The check's STATUS comes from the reporter's answer, set in C#. The agent's review sits last
 * and is advice: nothing acts on it.
 */
export function ClaimChain({ check }) {
  return (
    <ol className={styles.chain}>
      <Voice step={1} icon={Wrench} who="The technician said" tone="neutral">
        {check.workOrderResolutionNote ? (
          // Verbatim — the claim this check is testing.
          <blockquote className={styles.voiceQuote}>{check.workOrderResolutionNote}</blockquote>
        ) : (
          <p className={styles.voiceEmpty}>No resolution note was recorded on this work order.</p>
        )}
        <p className={styles.voiceWhen}>Completed {formatInstant(check.workOrderCompletedAt)}</p>
      </Voice>

      <Voice step={2} icon={MessageSquareQuote} who="The reporter says" tone={answerTone(check.reporterConfirmed)}>
        <ReporterAnswer check={check} />
      </Voice>

      <Voice step={3} icon={Bot} who="The agent's review" tone="agent" badge="Advice">
        <AgentReview check={check} />
      </Voice>
    </ol>
  );
}

function answerTone(confirmed) {
  if (confirmed === true) return 'yes';
  if (confirmed === false) return 'no';
  return 'waiting';
}

function Voice({ step, icon: Icon, who, tone, badge, children }) {
  return (
    <li className={clsx(styles.voice, styles[`voice-${tone}`])}>
      <header className={styles.voiceHead}>
        <span className={styles.voiceStep}>{step}</span>
        <Icon aria-hidden="true" strokeWidth={1.8} />
        <span className={styles.voiceWho}>{who}</span>
        {badge ? <span className={styles.voiceBadge}>{badge}</span> : null}
      </header>
      <div className={styles.voiceBody}>{children}</div>
    </li>
  );
}

/**
 * The answer, or the reason there is none. `reporterConfirmed` is null until answered — null
 * is "no answer", never "no" — and an Expired check says why it was given up on.
 */
function ReporterAnswer({ check }) {
  if (check.reporterConfirmed === null || check.reporterConfirmed === undefined) {
    let body = 'Asked, and not answered yet.';
    if (check.status === 'Pending') body = 'Not asked yet — the check is still inside its waiting period.';
    if (check.status === 'Expired') body = `Never answered. ${check.expiredReason ?? ''}`.trim();

    return (
      <p className={styles.voiceWaiting}>
        <Hourglass aria-hidden="true" />
        {body}
      </p>
    );
  }

  const Icon = check.reporterConfirmed ? CircleCheck : CircleX;
  return (
    <>
      <p className={styles.verdictLine}>
        <Icon aria-hidden="true" />
        {check.reporterConfirmed ? 'Yes — the problem is fixed.' : 'No — the problem is not fixed.'}
      </p>
      {check.reporterComment ? <blockquote className={styles.voiceQuote}>{check.reporterComment}</blockquote> : null}
      <p className={styles.voiceWhen}>Answered {formatInstant(check.reporterRespondedAt)}</p>
    </>
  );
}

/**
 * The VerificationAgent's verdict, reason and evidence, verbatim. "Not judged" has two
 * meanings, told apart by AgentQueuedAt: not handed over yet, or handed over and waiting.
 */
function AgentReview({ check }) {
  if (!check.agentOutcome) {
    return (
      <p className={styles.voiceWaiting}>
        <Hourglass aria-hidden="true" />
        {check.agentQueuedAt
          ? `Handed to the agent ${formatInstant(check.agentQueuedAt)}; no verdict yet.`
          : 'Not handed to the agent yet — that happens once the reporter answers, or stays silent past the response window.'}
      </p>
    );
  }

  return (
    <>
      <AgentOutcomePill outcome={check.agentOutcome} />
      {check.agentReason ? <blockquote className={styles.voiceQuote}>{check.agentReason}</blockquote> : null}

      <p className={styles.evidenceLabel}>Evidence</p>
      {/* Null is not empty: an outcome with no readable evidence says so. */}
      {check.agentEvidence && check.agentEvidence.length > 0 ? (
        <ul className={styles.evidence}>
          {check.agentEvidence.map((item, index) => (
            // eslint-disable-next-line react/no-array-index-key
            <li key={index}>{item}</li>
          ))}
        </ul>
      ) : (
        <p className={styles.voiceEmpty}>No evidence was recorded with this verdict.</p>
      )}

      <p className={styles.voiceWhen}>Recorded beside the status for you to weigh — it changes nothing by itself.</p>
    </>
  );
}

export default ClaimChain;
