import clsx from 'clsx';
import { Bot, Cable, ChevronDown, CircleAlert } from 'lucide-react';
import { useState } from 'react';

import { formatInstant } from '../../../components/ui/format';
import { Pill } from '../../../components/ui/Pill';
import { describeStep, durationLabel, prettyJson } from '../../reports/services/agentSteps';
import { answerTypeLabel } from '../../reports/services/reportsApi';
import styles from '../workflows.module.css';

const OUTCOME_TONES = { ok: 'green', neutral: 'slate', failed: 'red' };

// The agent writes snake_case answer types; the reports service labels the C# names.
const AGENT_ANSWER_TYPES = { yes_no: 'YesNo', single_select: 'SingleSelect', short_text: 'ShortText' };

/** "Yes / No" for a known answer type; an unknown one is shown exactly as the agent wrote it. */
function answerLabel(type) {
  const name = AGENT_ANSWER_TYPES[type];
  return name ? answerTypeLabel(name) : type;
}

function RawRecord({ step }) {
  const [open, setOpen] = useState(false);
  return (
    <div className={styles.raw}>
      <button type="button" className={styles.rawToggle} onClick={() => setOpen((current) => !current)} aria-expanded={open}>
        <ChevronDown aria-hidden="true" data-open={open} />
        {open ? 'Hide raw' : 'Show raw'}
      </button>
      {open ? (
        <dl className={styles.rawBody}>
          <dt>ValidationResult</dt>
          <dd>
            <pre>{step.validationResult ?? 'null'}</pre>
          </dd>
          <dt>ToolCallsJson</dt>
          <dd>
            <pre>{prettyJson(step.toolCallsJson) ?? 'null'}</pre>
          </dd>
          <dt>PayloadJson</dt>
          <dd>
            <pre>{prettyJson(step.payloadJson) ?? 'null'}</pre>
          </dd>
        </dl>
      ) : null}
    </div>
  );
}

/**
 * The workflow's audit trail — every agent run and every tool call, in the order the API sent
 * it (oldest first). Each row is readable first: who, what kind, a plain outcome, the time,
 * and one sentence from `describeStep`. The stored record is behind "Show raw", verbatim —
 * never the first thing a reader has to parse, and never hidden either.
 *
 * NotFound is grey and not a failure: a tool that found nothing answered its question.
 * Durations are not summed — an agent run's time already includes its tool calls.
 *
 * `showTally` off lets a caller that splits the trail by workflow put one tally above all of it.
 */
export function AuditTrail({ steps, showTally = true }) {
  const described = steps.map((step) => ({ step, info: describeStep(step) }));
  const agentRuns = described.filter(({ info }) => info.kind === 'agent').length;
  const failures = described.filter(({ info }) => info.outcome.failed).length;

  return (
    <>
      {showTally ? (
      <p className={styles.trailTally}>
        {agentRuns} agent {agentRuns === 1 ? 'run' : 'runs'} · {steps.length - agentRuns} tool{' '}
        {steps.length - agentRuns === 1 ? 'call' : 'calls'}
        {failures > 0 ? <span className={styles.trailFailures}> · {failures} failed</span> : null}
      </p>
      ) : null}
      <ol className={styles.trail}>
        {described.map(({ step, info }, index) => {
          const Icon = info.kind === 'agent' ? Bot : Cable;
          return (
            <li
              key={step.id}
              className={clsx(
                styles.trailItem,
                info.kind === 'agent' ? styles.trailAgent : styles.trailTool,
                info.outcome.failed && styles.trailFailed,
              )}
              style={{ animationDelay: `${Math.min(index, 10) * 40}ms` }}
            >
              <span className={styles.trailMarker} aria-hidden="true">
                <Icon strokeWidth={1.8} />
              </span>
              <div className={styles.trailBody}>
                <header className={styles.trailHead}>
                  <span className={styles.trailAgentName}>{step.agentName}</span>
                  {info.kind === 'agent' ? (
                    <span className={styles.trailKind}>Agent run</span>
                  ) : (
                    <span className={clsx(styles.trailKind, 'mx-mono')}>{info.tool.tool}</span>
                  )}
                  <Pill tone={OUTCOME_TONES[info.outcome.tone] ?? 'slate'}>{info.outcome.label}</Pill>
                  <span className={styles.trailMeta}>
                    <span>{durationLabel(step, info.kind)}</span>
                    <span>{formatInstant(step.createdAt)}</span>
                  </span>
                </header>

                <p className={styles.trailSummary}>{info.summary}</p>

                {step.errorMessage ? (
                  <p className={styles.trailReason}>
                    <CircleAlert aria-hidden="true" />
                    <span>
                      <strong>Reason:</strong> {step.errorMessage}
                    </span>
                  </p>
                ) : null}

                {/* Read-only: the questions as the agent wrote them. Nothing here collects an
                    answer — the reporter answers on the phone, in a form. */}
                {info.questions && info.questions.length > 0 ? (
                  <ol className={styles.questions}>
                    {info.questions.map((question, questionIndex) => (
                      // eslint-disable-next-line react/no-array-index-key
                      <li key={questionIndex}>
                        <span>{question.question_text}</span>
                        <span className={styles.questionMeta}>
                          {answerLabel(question.answer_type)}
                          {Array.isArray(question.options) && question.options.length > 0
                            ? ` · ${question.options.join(' / ')}`
                            : ''}
                        </span>
                      </li>
                    ))}
                  </ol>
                ) : null}

                <RawRecord step={step} />
              </div>
            </li>
          );
        })}
      </ol>
    </>
  );
}

export default AuditTrail;
