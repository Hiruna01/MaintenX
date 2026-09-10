/**
 * The clarifying questions an agent step produced, rendered read-only.
 *
 * DELIBERATELY NOT A FORM. The questions are stored and displayed; nothing here collects
 * an answer and there is no follow-up round. The answer control each question asks for
 * (`answerType`, and its options) is shown as a label so the finished shape stays
 * obvious, without this page becoming the thing that submits it.
 */
export function ClarifyingQuestions({ questions }) {
  return (
    <ol className="questions">
      {questions.map((question, index) => (
        // No stable id comes back from the agent, and the list is never reordered or
        // filtered — it is rendered once, exactly as stored.
        // eslint-disable-next-line react/no-array-index-key
        <li key={index} className="questions__item">
          <p className="questions__text">{question.question_text}</p>

          <p className="questions__meta">
            Answer: {question.answer_type}
            {Array.isArray(question.options) && question.options.length > 0
              ? ` — ${question.options.join(', ')}`
              : ''}
          </p>
        </li>
      ))}
    </ol>
  );
}

export default ClarifyingQuestions;
