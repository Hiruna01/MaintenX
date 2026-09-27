import { Well } from '../../../components/ui/Panel';
import { describeNoQuestionCount, formatPercent } from '../services/verificationApi';
import styles from '../verification.module.css';

function Stat({ label, value, detail }) {
  return (
    <div className={styles.stat}>
      <dt>{label}</dt>
      <dd className={styles.statValue}>{value}</dd>
      <dd className={styles.statDetail}>{detail}</dd>
    </div>
  );
}

/**
 * Clarification efficiency as four figures, each the API's, each with what it was counted from.
 * A median of nothing is null from the API and reads "—" here, never "0 h".
 */
export function ClarificationStats({ clarification }) {
  const median = clarification.medianHoursToAnswer;
  const noMedian = median === null || median === undefined;

  return (
    <Well>
      <dl className={styles.stats}>
        <Stat
          label="Needed no questions"
          value={formatPercent(clarification.noQuestionRate)}
          detail={describeNoQuestionCount(clarification)}
        />
        <Stat
          label="Questions per report"
          value={Number(clarification.averageQuestionsPerReport).toFixed(2)}
          detail={`${clarification.questionsAsked} asked across ${clarification.reportsWithQuestions} reports that needed any`}
        />
        <Stat
          label="Answered"
          value={formatPercent(clarification.answerRate)}
          detail={`${clarification.questionsAnswered} of ${clarification.questionsAsked} questions`}
        />
        <Stat
          label="Median time to answer"
          value={noMedian ? '—' : `${Number(median).toLocaleString()} h`}
          detail={noMedian ? 'Nothing answered yet' : 'From asked to answered'}
        />
      </dl>
    </Well>
  );
}

export default ClarificationStats;
