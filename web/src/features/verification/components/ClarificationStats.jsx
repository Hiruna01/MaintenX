import { formatPercent } from '../services/verificationApi';

/**
 * Clarification efficiency as four figures, each the API's, each with what it was counted from.
 * A median of nothing is null from the API and reads "—" here, never "0 h".
 */
export function ClarificationStats({ clarification }) {
  const median = clarification.medianHoursToAnswer;

  return (
    <dl className="summary-panel__grid metrics-stats">
      <div className="summary-stat">
        <dt className="summary-stat__label">Needed no questions</dt>
        <dd className="summary-stat__value">{formatPercent(clarification.noQuestionRate)}</dd>
        <dd className="summary-stat__detail">
          {clarification.reportsWithNoQuestions} of {clarification.reportsClarified} clarified reports
        </dd>
      </div>

      <div className="summary-stat">
        <dt className="summary-stat__label">Questions per report</dt>
        <dd className="summary-stat__value">{Number(clarification.averageQuestionsPerReport).toFixed(2)}</dd>
        <dd className="summary-stat__detail">
          {clarification.questionsAsked} asked across {clarification.reportsWithQuestions} reports that needed any
        </dd>
      </div>

      <div className="summary-stat">
        <dt className="summary-stat__label">Answered</dt>
        <dd className="summary-stat__value">{formatPercent(clarification.answerRate)}</dd>
        <dd className="summary-stat__detail">
          {clarification.questionsAnswered} of {clarification.questionsAsked} questions
        </dd>
      </div>

      <div className="summary-stat">
        <dt className="summary-stat__label">Median time to answer</dt>
        <dd className="summary-stat__value">
          {median === null || median === undefined ? '—' : `${Number(median).toLocaleString()} h`}
        </dd>
        <dd className="summary-stat__detail">
          {median === null || median === undefined ? 'Nothing answered yet' : 'From asked to answered'}
        </dd>
      </div>
    </dl>
  );
}

export default ClarificationStats;
