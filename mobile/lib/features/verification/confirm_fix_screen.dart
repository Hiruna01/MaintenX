import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../../widgets/surfaces.dart';
import '../reports/my_reports_screen.dart';
import '../reports/report.dart' show formatTimestamp;
import '../reports/reports_api.dart';
import 'verification.dart';
import 'verification_api.dart';
import 'verification_status_chip.dart';

/// One repair, and one question about it: "Is the problem fixed?"
///
/// THIS IS A FORM, NOT A CHAT. What the reporter said, what the technician did and when —
/// then a yes/no with no default and an optional comment capped at 300 characters, sent in
/// ONE POST. No bubbles, no send button, no thread, and nothing replies to the comment: the
/// API takes one answer per check and a second is a 409.
///
/// Once answered, the screen shows where the check has got to instead of the form — the
/// status the answer gave it (set in C#), when it was handed to the automated review, and
/// that review's label once there is one. It does not replay the comment: the status is the
/// record, not a transcript of what was said.
class ConfirmFixScreen extends ConsumerStatefulWidget {
  const ConfirmFixScreen({super.key, required this.checkId});

  /// Nested under the pending list: `/verifications/42`.
  static const String subPath = ':id';

  static String location(int checkId) => '/verifications/$checkId';

  /// Null when the route carried something that is not an id — rendered as an error, not a
  /// crash.
  final int? checkId;

  @override
  ConsumerState<ConfirmFixScreen> createState() => _ConfirmFixScreenState();
}

class _ConfirmFixScreenState extends ConsumerState<ConfirmFixScreen> {
  final _commentController = TextEditingController();

  /// Starts null: a default would be an answer the reporter never gave.
  bool? _fixed;

  Map<String, String> _errors = const {};
  String? _submitError;
  bool _isSubmitting = false;

  @override
  void dispose() {
    _commentController.dispose();
    super.dispose();
  }

  void _clearError(String field) {
    if (_errors.containsKey(field)) {
      setState(() => _errors = {..._errors}..remove(field));
    }
  }

  Future<void> _submit(int checkId) async {
    final errors = validateConfirmation(fixed: _fixed, comment: _commentController.text);
    setState(() {
      _errors = errors;
      _submitError = null;
    });
    if (errors.isNotEmpty) return;

    setState(() => _isSubmitting = true);

    try {
      await ref.read(verificationApiProvider).confirm(
            checkId,
            fixed: _fixed!,
            comment: _commentController.text,
          );

      if (!mounted) return;
      _refreshEverythingThatShowsThisCheck(checkId);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Answer recorded. Thank you.')),
      );
    } on ApiException catch (error) {
      if (!mounted) return;
      if (error.statusCode == 409) {
        // Answered elsewhere, or no longer waiting. The form is stale; show the check as it
        // really is now, and say why the answer was not taken.
        _refreshEverythingThatShowsThisCheck(checkId);
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(error.message)));
      } else {
        setState(() => _submitError = error.message);
      }
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  /// The detail re-reads and renders the new status in place of the form; the pending list
  /// and the report list are stale too — the report row carries this check's status.
  void _refreshEverythingThatShowsThisCheck(int checkId) {
    ref.invalidate(verificationDetailProvider(checkId));
    ref.invalidate(pendingVerificationsProvider);
    ref.invalidate(reportsPageProvider);
  }

  @override
  Widget build(BuildContext context) {
    final checkId = widget.checkId;
    if (checkId == null) {
      return Scaffold(
        backgroundColor: MxColors.surface,
        appBar: _appBar(context),
        body: const ErrorView(
          title: 'Not a repair',
          message: 'This link does not point at a repair to confirm.',
        ),
      );
    }

    final detail = ref.watch(verificationDetailProvider(checkId));

    return Scaffold(
      backgroundColor: MxColors.surface,
      appBar: _appBar(context),
      body: SafeArea(
        child: detail.when(
          loading: () => const LoadingView(message: 'Loading the repair…'),
          error: (error, _) {
            final status = error is ApiException ? error.statusCode : null;
            return switch (status) {
              404 => const ErrorView(
                  title: 'Repair not found',
                  message: 'There is no repair check with this id.',
                ),
              // Told apart from a 404 by the API: the check exists, on someone else's report.
              403 => const ErrorView(
                  title: 'Not your report',
                  message: 'This repair was for a fault somebody else reported, '
                      'so it is theirs to confirm.',
                ),
              _ => ErrorView(
                  title: 'Could not load the repair',
                  message: error is ApiException ? error.message : 'Could not reach the API.',
                  onRetry: () => ref.invalidate(verificationDetailProvider(checkId)),
                ),
            };
          },
          data: (check) {
            final content = RefreshIndicator(
              color: MxColors.ink,
              backgroundColor: MxColors.surface,
              onRefresh: () => ref.refresh(verificationDetailProvider(checkId).future),
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
                padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
                children: [
                  _Header(check: check),
                  const SizedBox(height: 22),
                  _Claim(check: check),
                  const SizedBox(height: 26),
                  if (check.isAwaitingAnswer) _form(check) else _StatusPanel(check: check),
                ],
              ),
            );
            if (!check.isAwaitingAnswer) return content;

            // The form's one action stays in reach under the scrolling claim.
            return Column(
              children: [
                Expanded(child: content),
                MxActionBar(
                  children: [
                    FilledButton(
                      onPressed: _isSubmitting ? null : () => _submit(check.id),
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
                          : const Text('Submit answer'),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      'You can answer once. Facilities read your answer; nobody replies to it here.',
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ),
              ],
            );
          },
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

  Widget _form(VerificationDetail check) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Is the problem fixed?', style: theme.textTheme.titleLarge),
        const SizedBox(height: 4),
        Text(
          'Think about the last few days, not just today.',
          style: theme.textTheme.bodySmall,
        ),
        const SizedBox(height: 14),
        SegmentedButton<bool>(
          segments: const [
            ButtonSegment(value: true, label: Text("Yes, it's fixed")),
            ButtonSegment(value: false, label: Text('No, still broken')),
          ],
          // Starts with neither chosen: a default would be an answer the reporter never gave.
          selected: {if (_fixed != null) _fixed!},
          emptySelectionAllowed: true,
          showSelectedIcon: false,
          onSelectionChanged: _isSubmitting
              ? null
              : (selection) {
                  if (selection.isEmpty) return;
                  setState(() => _fixed = selection.first);
                  _clearError('fixed');
                },
        ),
        if (_errors['fixed'] != null)
          Padding(
            padding: const EdgeInsets.only(top: 8, left: 4),
            child: Text(
              _errors['fixed']!,
              style: theme.textTheme.bodySmall?.copyWith(color: MxColors.red),
            ),
          ),
        const SizedBox(height: 20),
        AppFormField(
          label: 'Anything to add? (optional)',
          hintText: 'e.g. it cut out again on Tuesday',
          controller: _commentController,
          errorText: _errors['comment'],
          maxLength: maxCommentLength,
          maxLines: 3,
          enabled: !_isSubmitting,
          onChanged: (_) => _clearError('comment'),
        ),
        if (_submitError != null)
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
      ],
    );
  }
}

/// The screen's headline: the question while it is open, what became of it once not.
class _Header extends StatelessWidget {
  const _Header({required this.check});

  final VerificationDetail check;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final (title, subtitle) = check.isAwaitingAnswer
        ? ('Is it fixed?', 'A few days on from the repair, tell facilities whether it held.')
        : check.isAnswered
            ? ('Thanks, your answer is in', 'Here is where this repair has got to.')
            : ('Repair check', 'Here is where this repair has got to.');

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (check.isAwaitingAnswer) ...[
          // The question is waiting on its reader — iris, as everywhere in the app.
          Container(
            padding: const EdgeInsets.fromLTRB(8, 5, 11, 5),
            decoration: BoxDecoration(
              color: MxColors.irisSoft,
              borderRadius: BorderRadius.circular(999),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(LucideIcons.circleHelp, size: 14, color: MxColors.iris),
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
          const SizedBox(height: 14),
        ],
        Text(title, style: theme.textTheme.headlineMedium),
        const SizedBox(height: 6),
        Text(subtitle, style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite)),
      ],
    );
  }
}

/// What was reported and what the technician says they did — the claim being checked, as
/// two joined panels read top to bottom. Both verbatim and selectable: the note is
/// evidence, and a note cut short can drop the clause that matters.
class _Claim extends StatelessWidget {
  const _Claim({required this.check});

  final VerificationDetail check;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final body = theme.textTheme.bodyLarge?.copyWith(height: 1.45);
    final meta = theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        MxWell(
          radius: MxRadii.lg,
          padding: const EdgeInsets.fromLTRB(18, 16, 18, 20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const MxPanelLabel('What you reported', icon: LucideIcons.fileText),
              const SizedBox(height: 8),
              SelectableText(check.reportDescription, style: body),
              const SizedBox(height: 8),
              Text('${check.assetName}, ${check.assetTag}', style: meta),
            ],
          ),
        ),
        const SizedBox(height: 6),
        MxJoined(
          child: MxWell(
            radius: MxRadii.lg,
            padding: const EdgeInsets.fromLTRB(18, 20, 18, 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const MxPanelLabel('What the technician did', icon: LucideIcons.wrench),
                const SizedBox(height: 8),
                SelectableText(
                  check.resolutionNote ?? 'No note was recorded for this repair.',
                  style: check.resolutionNote == null
                      ? body?.copyWith(color: MxColors.graphite)
                      : body,
                ),
                const SizedBox(height: 8),
                Text('Completed ${formatTimestamp(check.completedAt)}', style: meta),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

/// Where the check has got to, in place of the form. What the answer did is C#; what the
/// automated review thinks is shown in its own panel beside it, as a review, never as the
/// status.
class _StatusPanel extends StatelessWidget {
  const _StatusPanel({required this.check});

  final VerificationDetail check;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final meta = theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular);
    final outcome = check.agentOutcome;
    final hasReview = outcome != null || check.agentQueuedAt != null;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Container(
          padding: const EdgeInsets.all(18),
          decoration: BoxDecoration(
            color: MxColors.surface,
            borderRadius: BorderRadius.circular(MxRadii.lg),
            border: Border.all(color: MxColors.hairline),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  const MxPanelLabel('Status'),
                  const SizedBox(width: 10),
                  VerificationStatusChip(status: check.status),
                ],
              ),
              const SizedBox(height: 10),
              Text(describeForReporter(check.status), style: theme.textTheme.bodyLarge),
              if (check.status == VerificationStatuses.pending) ...[
                const SizedBox(height: 6),
                Text('We will ask from ${formatTimestamp(check.dueAt)}.', style: meta),
              ],
              if (check.isAnswered) ...[
                const SizedBox(height: 14),
                const Divider(),
                const SizedBox(height: 14),
                Row(
                  children: [
                    MxIconTile(
                      icon: check.reporterConfirmed == true ? LucideIcons.check : LucideIcons.x,
                      size: 36,
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            // Null is "not answered", which isAnswered has already ruled out.
                            'Your answer: ${check.reporterConfirmed == true ? "Yes, it's fixed" : 'No, still broken'}',
                            style: theme.textTheme.titleSmall,
                          ),
                          Text(formatTimestamp(check.reporterRespondedAt), style: meta),
                        ],
                      ),
                    ),
                  ],
                ),
              ],
            ],
          ),
        ),
        if (hasReview) ...[
          const SizedBox(height: 12),
          MxWell(
            radius: MxRadii.lg,
            padding: const EdgeInsets.all(18),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const MxPanelLabel('Automated review', icon: LucideIcons.fileSearch),
                const SizedBox(height: 10),
                if (outcome != null) ...[
                  AgentOutcomeChip(outcome: outcome),
                  const SizedBox(height: 8),
                  Text(describeAgentOutcome(outcome), style: theme.textTheme.bodyMedium),
                  if (check.agentReason != null) ...[
                    const SizedBox(height: 6),
                    SelectableText(
                      check.agentReason!,
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ] else
                  Text(
                    'Sent for review ${formatTimestamp(check.agentQueuedAt)}. If the review flags '
                    'anything, it will show here and on your report.',
                    style: theme.textTheme.bodyMedium,
                  ),
              ],
            ),
          ),
        ],
        const SizedBox(height: 18),
        OutlinedButton(
          onPressed: () => context.go(MyReportsScreen.path),
          child: const Text('See it on my reports'),
        ),
      ],
    );
  }
}
