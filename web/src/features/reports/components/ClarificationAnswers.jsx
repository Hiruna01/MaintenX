import { ANSWER_TYPES, answerTypeLabel, formatDateTime } from '../services/reportsApi';

/**
 * The clarification questions asked about this report, in the order the form renders them,
 * each with the reporter's answer once one has been given.
 *
 * READ-ONLY, AND NOT A TRANSCRIPT. These are the ClarificationQuestion rows — the working
 * copy — not the agent's payload. Each question names the bounded control it was answered
 * through (a yes/no toggle, a picker over fixed options, a capped short string), and each
 * takes one answer. Answers are given by the reporter from the mobile app; nothing here
 * collects one. There is no chat interface.
 */
export function ClarificationAnswers({ questions }) {
  return (
    <ol className="clarifications">
      {questions.map((question, index) => {
        const answered = question.answerText !== null && question.answerText !== undefined;

        return (
          <li key={question.id} className="clarifications__item">
            <div className="clarifications__head">
              <span className="clarifications__number">Q{index + 1}</span>
              <p className="clarifications__text">{question.questionText}</p>
              <span className="clarifications__type">{answerTypeLabel(question.answerType)}</span>
            </div>

            {question.answerType === ANSWER_TYPES.SingleSelect && Array.isArray(question.options) ? (
              <ul className="clarifications__options" aria-label="Options offered">
                {question.options.map((option) => (
                  <li
                    key={option}
                    className={option === question.answerText ? 'clarifications__option--chosen' : undefined}
                  >
                    {option}
                  </li>
                ))}
              </ul>
            ) : null}

            {answered ? (
              <p className="clarifications__answer">
                <span className="clarifications__answer-label">Answer</span>
                <span className="clarifications__answer-text">{question.answerText}</span>
                <span className="clarifications__answer-when">{formatDateTime(question.answeredAt)}</span>
              </p>
            ) : (
              <p className="clarifications__answer clarifications__answer--waiting">
                Waiting for the reporter to answer.
              </p>
            )}
          </li>
        );
      })}
    </ol>
  );
}

export default ClarificationAnswers;
