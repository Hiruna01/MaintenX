import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../../widgets/surfaces.dart';
import 'clarification.dart';
import 'clarifier_wait.dart';
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
///
/// Opened straight after filing ([waitForQuestions]), it first waits for the clarifier —
/// see [ClarifierWait] — and the form opens in place the moment its questions exist.
class ClarificationScreen extends ConsumerStatefulWidget {
  const ClarificationScreen({
    super.key,
    required this.reportId,
    this.waitForQuestions = false,
  });

  /// Nested under the reports list: `/reports/42/clarifications`.
  static const String subPath = ':id/clarifications';

  /// The query parameter that marks a report filed a moment ago: `?waiting=1`.
  static const String waitingParam = 'waiting';

  static String location(int reportId, {bool waitForQuestions = false}) =>
      '/reports/$reportId/clarifications${waitForQuestions ? '?$waitingParam=1' : ''}';

  /// Null when the route carried something that is not an id — rendered as an error, not
  /// a crash.
  final int? reportId;

  /// True when the report was filed a moment ago and the clarifier may not have answered
  /// yet. Otherwise the questions are read once, as they stand.
  final bool waitForQuestions;

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

  /// The questions [ClarifierWait] found. Null while it is still waiting.
  List<ClarificationQuestion>? _arrived;

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

  /// Display only: whether this question has something in its control yet, for the tick
  /// on its number and the count in the action bar. validate() is still the rule.
  bool _hasInput(ClarificationQuestion question) =>
      question.answerType == AnswerTypes.shortText
          ? _controllerFor(question.id).text.trim().isNotEmpty
          : _picked[question.id] != null;

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
        backgroundColor: MxColors.surface,
        appBar: _appBar(context),
        body: const ErrorView(
          title: 'Not a report',
          message: 'This link does not point at a report.',
        ),
      );
    }

    if (widget.waitForQuestions) {
      final arrived = _arrived;
      return Scaffold(
        backgroundColor: MxColors.surface,
        appBar: _appBar(context),
        body: SafeArea(
          child: AnimatedSwitcher(
            duration: MediaQuery.disableAnimationsOf(context)
                ? Duration.zero
                : const Duration(milliseconds: 250),
            child: arrived == null
                ? ClarifierWait(
                    reportId: reportId,
                    onQuestions: (questions) => setState(() => _arrived = questions),
                  )
                : KeyedSubtree(
                    key: const ValueKey('form'),
                    child: _body(reportId, arrived),
                  ),
          ),
        ),
      );
    }

    final questions = ref.watch(clarificationsProvider(reportId));

    return Scaffold(
      backgroundColor: MxColors.surface,
      appBar: _appBar(context),
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

  PreferredSizeWidget _appBar(BuildContext context) => AppBar(
        leading: Navigator.canPop(context)
            ? IconButton(
                tooltip: 'Back',
                icon: const Icon(LucideIcons.arrowLeft),
                onPressed: () => Navigator.maybePop(context),
              )
            : null,
      );

  Widget _body(int reportId, List<ClarificationQuestion> questions) {
    // Nothing asked is a normal outcome — a clear report needs no clarification.
    if (questions.isEmpty) {
      return const EmptyView(
        icon: LucideIcons.circleCheck,
        message: 'Nothing to answer. No questions were asked about this report.',
      );
    }

    // Answers go in all at once, so one answered question means the form is done. Said,
    // not replayed: this screen has no view of what was asked and answered before.
    if (questions.any((question) => question.isAnswered)) {
      return EmptyView(
        icon: LucideIcons.circleCheck,
        message: 'You have already answered these questions. Thank you.',
        action: FilledButton(
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
    final filled = questions.where(_hasInput).length;

    return Column(
      children: [
        Expanded(
          child: ListView(
            keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
            children: [
              // This whole form is waiting on its reader — the one place for iris here.
              Align(
                alignment: Alignment.centerLeft,
                child: Container(
                  padding: const EdgeInsets.fromLTRB(8, 5, 11, 5),
                  decoration: BoxDecoration(
                    color: MxColors.irisSoft,
                    borderRadius: BorderRadius.circular(999),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(LucideIcons.messageCircleQuestion, size: 14, color: MxColors.iris),
                      const SizedBox(width: 6),
                      Text(
                        'Waiting on you',
                        style: theme.textTheme.labelMedium?.copyWith(
                          color: MxColors.iris,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 14),
              Text(
                questions.length == 1 ? 'One quick question' : 'A few quick questions',
                style: theme.textTheme.headlineMedium,
              ),
              const SizedBox(height: 6),
              Text(
                'Facilities need a little more detail before they can act on your report. '
                'Answer every question, then submit the form once.',
                style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
              ),
              const SizedBox(height: 22),
              if (_submitError != null) ...[
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Padding(
                      padding: EdgeInsets.only(top: 2),
                      child: Icon(LucideIcons.circleAlert, size: 16, color: MxColors.red),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        _submitError!,
                        style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.red),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
              ],
              for (var i = 0; i < questions.length; i++) ...[
                _QuestionField(
                  key: ValueKey(questions[i].id),
                  index: i,
                  total: questions.length,
                  question: questions[i],
                  errorText: _errors[questions[i].id],
                  enabled: !_isSubmitting,
                  hasInput: _hasInput(questions[i]),
                  picked: _picked[questions[i].id],
                  textController: questions[i].answerType == AnswerTypes.shortText
                      ? _controllerFor(questions[i].id)
                      : null,
                  onPicked: (value) {
                    setState(() => _picked[questions[i].id] = value);
                    _clearError(questions[i].id);
                  },
                  onTyped: () {
                    // Rebuild for the tick and the count; clear any error being fixed.
                    setState(() {});
                    _clearError(questions[i].id);
                  },
                ),
                const SizedBox(height: 12),
              ],
            ],
          ),
        ),
        MxActionBar(
          children: [
            Row(
              children: [
                Text(
                  '$filled of ${questions.length} answered',
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: MxColors.graphite,
                    fontFeatures: MxType.tabular,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(99),
                    child: LinearProgressIndicator(
                      minHeight: 4,
                      value: filled / questions.length,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: _isSubmitting ? null : () => _submit(reportId, questions),
              style: FilledButton.styleFrom(
                disabledBackgroundColor: MxColors.ink2,
                disabledForegroundColor: Colors.white,
              ),
              child: _isSubmitting
                  ? const Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(
                            strokeWidth: 2,
                            color: Colors.white,
                            strokeCap: StrokeCap.round,
                          ),
                        ),
                        SizedBox(width: 12),
                        Text('Submitting…'),
                      ],
                    )
                  : const Text('Submit answers'),
            ),
          ],
        ),
      ],
    );
  }
}

/// One question and its ONE bounded control, on a grey panel. The number is a real
/// sequence — the form is read top to bottom — and ticks once its control has something in
/// it; the panel's edge turns red when validate() refuses it.
class _QuestionField extends StatelessWidget {
  const _QuestionField({
    super.key,
    required this.index,
    required this.total,
    required this.question,
    required this.errorText,
    required this.enabled,
    required this.hasInput,
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
  final bool hasInput;
  final String? picked;
  final TextEditingController? textController;
  final ValueChanged<String> onPicked;
  final VoidCallback onTyped;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 18),
      decoration: BoxDecoration(
        color: MxColors.well,
        borderRadius: BorderRadius.circular(MxRadii.lg),
        border: Border.all(
          color: errorText != null ? MxColors.redLine : Colors.transparent,
          width: 1.2,
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              AnimatedContainer(
                duration: const Duration(milliseconds: 180),
                width: 26,
                height: 26,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: hasInput ? MxColors.ink : MxColors.surface,
                  shape: BoxShape.circle,
                ),
                child: hasInput
                    ? const Icon(LucideIcons.check, size: 14, color: Colors.white)
                    : Text(
                        '${index + 1}',
                        style: theme.textTheme.labelMedium?.copyWith(
                          fontWeight: FontWeight.w600,
                          fontFeatures: MxType.tabular,
                        ),
                      ),
              ),
              const SizedBox(width: 10),
              Text(
                'Question ${index + 1} of $total',
                style: theme.textTheme.labelMedium?.copyWith(
                  color: MxColors.graphite,
                  fontFeatures: MxType.tabular,
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            question.questionText,
            style: theme.textTheme.titleMedium?.copyWith(fontSize: 17, height: 1.35),
          ),
          const SizedBox(height: 14),
          _control(theme),
        ],
      ),
    );
  }

  Widget _control(ThemeData theme) {
    switch (question.answerType) {
      case AnswerTypes.yesNo:
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SegmentedButton<String>(
              segments: const [
                ButtonSegment(
                  value: YesNoAnswers.yes,
                  icon: Icon(LucideIcons.check, size: 18),
                  label: Text('Yes'),
                ),
                ButtonSegment(
                  value: YesNoAnswers.no,
                  icon: Icon(LucideIcons.x, size: 18),
                  label: Text('No'),
                ),
              ],
              // Starts with neither chosen: a default would be an answer the reporter
              // never gave.
              selected: {if (picked != null) picked!},
              emptySelectionAllowed: true,
              showSelectedIcon: false,
              onSelectionChanged: enabled
                  ? (selection) {
                      if (selection.isNotEmpty) onPicked(selection.first);
                    }
                  : null,
            ),
            if (errorText != null)
              Padding(
                padding: const EdgeInsets.only(top: 8, left: 4),
                child: Text(
                  errorText!,
                  style: theme.textTheme.bodySmall?.copyWith(color: MxColors.red),
                ),
              ),
          ],
        );

      case AnswerTypes.singleSelect:
        return AppDropdownField<String>(
          label: 'Choose one',
          value: picked,
          errorText: errorText,
          fillColor: MxColors.surface,
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
          fillColor: MxColors.surface,
          onChanged: (_) => onTyped(),
        );

      default:
        // Unreachable: the screen refuses the whole form before an unknown type gets here.
        throw StateError('Unrenderable answer type ${question.answerType}');
    }
  }
}
