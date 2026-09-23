import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import 'clarification.dart';
import 'my_reports_screen.dart';
import 'reports_api.dart';

/// The agent's clarification questions about one report, rendered as a FORM.
///
/// THIS IS NOT A CHAT, AND NOTHING ON IT MAY TURN INTO ONE. There is no chat interface
/// anywhere in this system:
///   * every question is one bounded control, chosen by its `AnswerType` NAME —
///     YesNo -> a segmented yes/no, SingleSelect -> a dropdown of exactly the options the
///     API supplied, ShortText -> a text field capped at 100 characters with a counter;
///   * an answer type this app does not know gets NO free-text fallback — the form
///     refuses to render instead, because a fallback text box is how a message box would
///     get in;
///   * no bubbles, no "send" button, no transcript of earlier questions — the whole form
///     goes in ONE POST, 204, and the exchange is over. A report already answered shows
///     that it is done, not what was said.
class ClarificationScreen extends ConsumerStatefulWidget {
  const ClarificationScreen({super.key, required this.reportId});

  /// Nested under the reports list: `/reports/42/clarifications`.
  static const String subPath = ':id/clarifications';

  static String location(int reportId) => '/reports/$reportId/clarifications';

  /// Null when the route carried something that is not an id — rendered as an error, not
  /// a crash.
  final int? reportId;

  @override
  ConsumerState<ClarificationScreen> createState() => _ClarificationScreenState();
}

class _ClarificationScreenState extends ConsumerState<ClarificationScreen> {
  /// Question id -> the picked value, for YesNo and SingleSelect. ShortText answers live in
  /// their controllers and are read at submit time.
  final Map<int, String> _picked = {};
  final Map<int, TextEditingController> _textControllers = {};

  Map<int, String> _errors = const {};
  String? _submitError;
  bool _isSubmitting = false;

  @override
  void dispose() {
    for (final controller in _textControllers.values) {
      controller.dispose();
    }
    super.dispose();
  }

  TextEditingController _controllerFor(int questionId) =>
      _textControllers.putIfAbsent(questionId, TextEditingController.new);

  /// Every answer, in the order the questions are shown.
  Map<int, String> _answers(List<ClarificationQuestion> questions) => {
        for (final question in questions)
          question.id: question.answerType == AnswerTypes.shortText
              ? _controllerFor(question.id).text
              : (_picked[question.id] ?? ''),
      };

  void _clearError(int questionId) {
    if (_errors.containsKey(questionId)) {
      setState(() => _errors = {..._errors}..remove(questionId));
    }
  }

  Future<void> _submit(int reportId, List<ClarificationQuestion> questions) async {
    final answers = _answers(questions);
    final errors = validateClarificationAnswers(questions, answers);
    setState(() {
      _errors = errors;
      _submitError = null;
    });
    if (errors.isNotEmpty) return;

    setState(() => _isSubmitting = true);

    try {
      // ONE request with the whole form. Not one per question, and nothing after it.
      await ref.read(reportsApiProvider).submitAnswers(reportId, {
        for (final question in questions) question.id: answerToSend(question, answers[question.id]),
      });

      if (!mounted) return;
      // The report has moved on — its status and its "waiting on you" count are stale.
      ref.invalidate(reportsPageProvider);
      ref.invalidate(clarificationsProvider(reportId));
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Answers submitted. Thank you.')),
      );
      context.go(MyReportsScreen.path);
    } on ApiException catch (error) {
      if (mounted) setState(() => _submitError = error.message);
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final reportId = widget.reportId;
    if (reportId == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('More detail needed')),
        body: const ErrorView(
          title: 'Not a report',
          message: 'This link does not point at a report.',
        ),
      );
    }

    final questions = ref.watch(clarificationsProvider(reportId));

    return Scaffold(
      appBar: AppBar(title: const Text('More detail needed')),
      body: SafeArea(
        child: questions.when(
          loading: () => const LoadingView(message: 'Loading questions…'),
          error: (error, _) {
            final notFound = error is ApiException && error.statusCode == 404;
            return ErrorView(
              title: notFound ? 'Report not found' : 'Could not load the questions',
              message: notFound
                  ? 'There is no report with this id.'
                  : error is ApiException
                      ? error.message
                      : 'Could not reach the API.',
              onRetry: notFound ? null : () => ref.invalidate(clarificationsProvider(reportId)),
            );
          },
          data: (data) => _body(reportId, data),
        ),
      ),
    );
  }

  Widget _body(int reportId, List<ClarificationQuestion> questions) {
    // Nothing asked is a normal outcome — a clear report needs no clarification.
    if (questions.isEmpty) {
      return const EmptyView(
        icon: Icons.check_circle_outline,
        message: 'Nothing to answer. No questions were asked about this report.',
      );
    }

    // Answers go in all at once, so one answered question means the form is done. Said,
    // not replayed: this screen has no view of what was asked and answered before.
    if (questions.any((question) => question.isAnswered)) {
      return EmptyView(
        icon: Icons.check_circle_outline,
        message: 'You have already answered these questions. Thank you.',
        action: FilledButton.tonal(
          onPressed: () => context.go(MyReportsScreen.path),
          child: const Text('Back to my reports'),
        ),
      );
    }

    // Fail closed: a question this app cannot draw a bounded control for is not given a
    // text box instead. The API requires every question answered, so the form cannot be
    // sent without it either.
    final unrenderable = questions.where((question) => !question.isRenderable).toList();
    if (unrenderable.isNotEmpty) {
      return ErrorView(
        title: 'This form cannot be shown',
        message: 'It contains a kind of question this version of the app does not know '
            '(${unrenderable.first.answerType}). Please update the app.',
      );
    }

    final theme = Theme.of(context);

    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        Text(
          'Facilities need a little more detail before they can act on your report. '
          'Answer every question, then submit the form once.',
          style: theme.textTheme.bodyMedium,
        ),
        const SizedBox(height: 20),
        if (_submitError != null) ...[
          Text(_submitError!, style: TextStyle(color: theme.colorScheme.error)),
          const SizedBox(height: 16),
        ],
        for (var i = 0; i < questions.length; i++)
          _QuestionField(
            key: ValueKey(questions[i].id),
            index: i,
            total: questions.length,
            question: questions[i],
            errorText: _errors[questions[i].id],
            enabled: !_isSubmitting,
            picked: _picked[questions[i].id],
            textController: questions[i].answerType == AnswerTypes.shortText
                ? _controllerFor(questions[i].id)
                : null,
            onPicked: (value) {
              setState(() => _picked[questions[i].id] = value);
              _clearError(questions[i].id);
            },
            onTyped: () => _clearError(questions[i].id),
          ),
        const SizedBox(height: 8),
        FilledButton(
          onPressed: _isSubmitting ? null : () => _submit(reportId, questions),
          child: Text(_isSubmitting ? 'Submitting…' : 'Submit answers'),
        ),
      ],
    );
  }
}

/// One question and its ONE bounded control.
class _QuestionField extends StatelessWidget {
  const _QuestionField({
    super.key,
    required this.index,
    required this.total,
    required this.question,
    required this.errorText,
    required this.enabled,
    required this.picked,
    required this.textController,
    required this.onPicked,
    required this.onTyped,
  });

  final int index;
  final int total;
  final ClarificationQuestion question;
  final String? errorText;
  final bool enabled;
  final String? picked;
  final TextEditingController? textController;
  final ValueChanged<String> onPicked;
  final VoidCallback onTyped;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Question ${index + 1} of $total',
            style: theme.textTheme.labelMedium?.copyWith(color: theme.colorScheme.outline),
          ),
          const SizedBox(height: 4),
          Text(question.questionText, style: theme.textTheme.titleMedium),
          const SizedBox(height: 12),
          _control(theme),
        ],
      ),
    );
  }

  Widget _control(ThemeData theme) {
    switch (question.answerType) {
      case AnswerTypes.yesNo:
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: double.infinity,
              child: SegmentedButton<String>(
                segments: const [
                  ButtonSegment(value: YesNoAnswers.yes, label: Text('Yes')),
                  ButtonSegment(value: YesNoAnswers.no, label: Text('No')),
                ],
                // Starts with neither chosen: a default would be an answer the reporter
                // never gave.
                selected: {if (picked != null) picked!},
                emptySelectionAllowed: true,
                onSelectionChanged: enabled
                    ? (selection) {
                        if (selection.isNotEmpty) onPicked(selection.first);
                      }
                    : null,
              ),
            ),
            if (errorText != null)
              Padding(
                padding: const EdgeInsets.only(top: 6, left: 12),
                child: Text(
                  errorText!,
                  style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
                ),
              ),
          ],
        );

      case AnswerTypes.singleSelect:
        return AppDropdownField<String>(
          label: 'Choose one',
          value: picked,
          errorText: errorText,
          // Exactly the options the API supplied, in its order, as its strings — the API
          // matches the answer against them ordinally.
          items: [
            for (final option in question.options!)
              DropdownMenuItem(value: option, child: Text(option)),
          ],
          onChanged: (value) {
            if (enabled && value != null) onPicked(value);
          },
        );

      case AnswerTypes.shortText:
        return AppFormField(
          label: 'Your answer',
          controller: textController!,
          errorText: errorText,
          maxLength: maxAnswerLength,
          enabled: enabled,
          onChanged: (_) => onTyped(),
        );

      default:
        // Unreachable: the screen refuses the whole form before an unknown type gets here.
        throw StateError('Unrenderable answer type ${question.answerType}');
    }
  }
}
