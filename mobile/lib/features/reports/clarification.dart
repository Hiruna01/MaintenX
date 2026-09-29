import 'dart:convert';

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

/// Where the clarifier has got to on one report, read off `GET /api/reports/{id}`. Display
/// only — it picks which screen to draw while the reporter waits and decides nothing. The
/// web's `latestAgentRunState` reads the same rows for the same reason.
enum ClarifierProgress {
  /// Not finished yet: no clarifier run recorded, or its questions are still being written.
  running,

  /// Questions are waiting for the form.
  asked,

  /// The clarifier ran and needed nothing — a normal, common answer.
  nothingToAsk,

  /// The run could not produce questions. The report is still filed; nobody is asked.
  failed,
}

/// The name `WorkflowRunner` records the clarifier's run under
/// (`AgentRunResponse.ClarifierAgentName`).
const String clarifierAgentName = 'clarifier';

/// The name `WorkflowRunner` records the planner's run under (`PlanRules.PlannerAgentName`).
const String plannerAgentName = 'planner';

/// Reads [ClarifierProgress] from a `ReportDetailDto`.
///
/// Questions on the report are the answer on their own. Otherwise it is the latest
/// clarifier AGENT RUN — `ToolCallsJson` `"[]"`; the clarifier's tool calls carry the same
/// name — that says. The runner saves that step BEFORE it writes the question rows, so a
/// step whose payload holds questions while the report has none yet is still [running]:
/// the rows are a moment behind, and reading it as "nothing to ask" would send the reporter
/// away from a form that is about to exist.
///
/// With no clarifier run at all, the PLANNER may have decided there is nothing to ask: its
/// accepted plan (`validationResult` Ok) leaves the clarifier out, and no clarifier step will
/// ever come. That is [nothingToAsk]. A planner that failed or was rejected means the default
/// plan — the clarifier included — so the wait goes on.
ClarifierProgress readClarifierProgress(Map<String, dynamic> report) {
  final questions = (report['clarificationQuestions'] as List<dynamic>?) ?? const [];
  if (questions.isNotEmpty) return ClarifierProgress.asked;

  final steps = (report['agentSteps'] as List<dynamic>?) ?? const [];
  Map<String, dynamic>? latestRun;
  for (final step in steps.cast<Map<String, dynamic>>()) {
    if (step['agentName'] == clarifierAgentName && step['toolCallsJson'] == '[]') {
      latestRun = step;
    }
  }
  if (latestRun == null) {
    return _plannedWithoutClarifier(steps) ? ClarifierProgress.nothingToAsk : ClarifierProgress.running;
  }
  if (latestRun['validationResult'] != 'Ok') return ClarifierProgress.failed;

  try {
    final payload = jsonDecode(latestRun['payloadJson'] as String? ?? '');
    final asked = payload is Map<String, dynamic> ? payload['questions'] : null;
    if (asked is List) {
      return asked.isEmpty ? ClarifierProgress.nothingToAsk : ClarifierProgress.running;
    }
  } on FormatException {
    // Unreadable: keep waiting. The screen's own time limit ends the wait either way.
  }
  return ClarifierProgress.running;
}

/// Whether the latest accepted planner run delegated nothing to the clarifier.
bool _plannedWithoutClarifier(List<dynamic> steps) {
  Map<String, dynamic>? latestPlan;
  for (final step in steps.cast<Map<String, dynamic>>()) {
    if (step['agentName'] == plannerAgentName && step['toolCallsJson'] == '[]') {
      latestPlan = step;
    }
  }
  if (latestPlan == null || latestPlan['validationResult'] != 'Ok') return false;

  try {
    final payload = jsonDecode(latestPlan['payloadJson'] as String? ?? '');
    final planned = payload is Map<String, dynamic> ? payload['steps'] : null;
    if (planned is! List || planned.isEmpty) return false;
    return !planned.any((step) => step is Map<String, dynamic> && step['agent'] == clarifierAgentName);
  } on FormatException {
    return false;
  }
}
