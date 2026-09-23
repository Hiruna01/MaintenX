/// Mirrors the API's `AnswerType` enum, by NAME — and that is the whole list.
///
/// Every clarification comes back through a toggle, a picker over a fixed list, or a capped
/// short string. None of them is a message box, so there is no free-text turn for a
/// conversation to grow out of. There is no chat interface in this system.
class AnswerTypes {
  const AnswerTypes._();

  static const String yesNo = 'YesNo';
  static const String singleSelect = 'SingleSelect';
  static const String shortText = 'ShortText';

  static const Set<String> all = {yesNo, singleSelect, shortText};
}

/// The two strings a YesNo question is answered with.
class YesNoAnswers {
  const YesNoAnswers._();

  static const String yes = 'Yes';
  static const String no = 'No';
}

/// The API's cap on an answer (`SubmittedAnswer.AnswerText`, `ClarificationAnswer`), and the
/// agent's own short_text cap.
const int maxAnswerLength = 100;

/// Mirrors the API's `ClarificationQuestionDto` — enough to render ONE bounded form field.
class ClarificationQuestion {
  const ClarificationQuestion({
    required this.id,
    required this.questionText,
    required this.answerType,
    required this.options,
    required this.displayOrder,
    required this.answerText,
    required this.answeredAt,
  });

  final int id;
  final String questionText;
  final String answerType;

  /// The choices, exactly as the API supplied them. Null for every type but SingleSelect.
  final List<String>? options;
  final int displayOrder;

  /// Null while unanswered.
  final String? answerText;
  final String? answeredAt;

  bool get isAnswered => answerText != null;

  /// Whether this app can draw a bounded control for it. An unknown type — or a picker
  /// with nothing to pick — is NOT given a text box as a fallback: a free-text fallback is
  /// exactly how a chat box would get in.
  bool get isRenderable =>
      AnswerTypes.all.contains(answerType) &&
      (answerType != AnswerTypes.singleSelect || (options?.isNotEmpty ?? false));

  factory ClarificationQuestion.fromJson(Map<String, dynamic> json) {
    return ClarificationQuestion(
      id: json['id'] as int,
      questionText: json['questionText'] as String,
      answerType: json['answerType'] as String,
      options: (json['options'] as List<dynamic>?)?.cast<String>(),
      displayOrder: json['displayOrder'] as int,
      answerText: json['answerText'] as String?,
      answeredAt: json['answeredAt'] as String?,
    );
  }
}

/// The text that goes to the API for [question]. Typed text is trimmed; a picked value is
/// sent exactly as the API supplied it, because the API matches an option ordinally.
String answerToSend(ClarificationQuestion question, String? value) {
  final raw = value ?? '';
  return question.answerType == AnswerTypes.shortText ? raw.trim() : raw;
}

/// The form's validate(): a message per unanswered or invalid question, keyed by question
/// id. An empty map means the whole form may be sent.
///
/// Every question needs an answer because the API takes the whole form in one request and
/// refuses a partial one — a second round to finish it would be a conversation.
///
/// The API re-checks all of this, and more (a picked option must be one it offered). This
/// is here so the reporter is told before a round trip, not instead of the server's rule.
Map<int, String> validateClarificationAnswers(
  List<ClarificationQuestion> questions,
  Map<int, String> answers,
) {
  final errors = <int, String>{};

  for (final question in questions) {
    final answer = answerToSend(question, answers[question.id]);

    if (answer.isEmpty) {
      errors[question.id] = switch (question.answerType) {
        AnswerTypes.yesNo => 'Choose yes or no.',
        AnswerTypes.singleSelect => 'Choose one of the options.',
        _ => 'Answer this question.',
      };
    } else if (answer.length > maxAnswerLength) {
      // Can get past the field's counter: it counts what the reader sees, and the API
      // counts UTF-16 code units, which an emoji takes two of.
      errors[question.id] = 'Keep it to $maxAnswerLength characters.';
    } else if (question.answerType == AnswerTypes.singleSelect &&
        !(question.options?.contains(answer) ?? false)) {
      errors[question.id] = 'Choose one of the options.';
    }
  }

  return errors;
}
