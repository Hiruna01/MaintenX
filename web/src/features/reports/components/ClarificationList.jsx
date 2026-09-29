import clsx from 'clsx';
import { Check, Hourglass } from 'lucide-react';

import { formatInstant } from '../../../components/ui/format';
import { Pill } from '../../../components/ui/Pill';
import { ANSWER_TYPES, answerTypeLabel } from '../services/reportsApi';
import styles from '../reports.module.css';

/**
 * The clarification questions asked about this report, in the order the form renders them,
 * each with the reporter's answer once one has been given.
 *
 * READ-ONLY, AND NOT A TRANSCRIPT. These are the ClarificationQuestion rows — the working
 * copy — not the agent's payload. Each names the bounded control it was answered through and
 * takes one answer. Answers are given from the mobile app; nothing here collects one. There
 * is no chat interface.
 */
export function ClarificationList({ questions }) {
  return (
    <ol className={styles.questions}>
      {questions.map((question, index) => {
        const answered = question.answerText !== null && question.answerText !== undefined;

        return (
          <li key={question.id} className={clsx(styles.question, answered ? styles.questionAnswered : styles.questionWaiting)}>
            <div className={styles.questionHead}>
              <span className={styles.questionNumber}>Q{index + 1}</span>
              <p className={styles.questionText}>{question.questionText}</p>
              <Pill tone="slate" dot={false}>
                {answerTypeLabel(question.answerType)}
              </Pill>
            </div>

            {question.answerType === ANSWER_TYPES.SingleSelect && Array.isArray(question.options) ? (
              <ul className={styles.options} aria-label="Options offered">
                {question.options.map((option) => (
                  <li key={option} className={option === question.answerText ? styles.optionChosen : undefined}>
                    {option === question.answerText ? <Check aria-hidden="true" /> : null}
                    {option}
                  </li>
                ))}
              </ul>
            ) : null}

            {answered ? (
              <p className={styles.answer}>
                <span className={styles.answerLabel}>Answer</span>
                <span className={styles.answerText}>{question.answerText}</span>
                <span className={styles.answerWhen}>{formatInstant(question.answeredAt)}</span>
              </p>
            ) : (
              <p className={styles.answerWaiting}>
                <Hourglass aria-hidden="true" /> Waiting for the reporter to answer.
              </p>
            )}
          </li>
        );
      })}
    </ol>
  );
}

export default ClarificationList;
