import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
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
        appBar: AppBar(title: const Text('Is it fixed?')),
        body: const ErrorView(
          title: 'Not a repair',
          message: 'This link does not point at a repair to confirm.',
        ),
      );
    }

    final detail = ref.watch(verificationDetailProvider(checkId));

    return Scaffold(
      appBar: AppBar(title: const Text('Is it fixed?')),
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
          data: (check) => RefreshIndicator(
            onRefresh: () => ref.refresh(verificationDetailProvider(checkId).future),
            child: ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.all(24),
              children: [
                _Claim(check: check),
                const SizedBox(height: 24),
                if (check.isAwaitingAnswer) _form(check) else _StatusPanel(check: check),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _form(VerificationDetail check) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Is the problem fixed?', style: theme.textTheme.titleMedium),
        const SizedBox(height: 12),
        SegmentedButton<bool>(
          segments: const [
            ButtonSegment(value: true, label: Text("Yes, it's fixed")),
            ButtonSegment(value: false, label: Text('No, still broken')),
          ],
          selected: {if (_fixed != null) _fixed!},
          emptySelectionAllowed: true,
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
            padding: const EdgeInsets.only(top: 6, left: 12),
            child: Text(
              _errors['fixed']!,
              style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
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
        if (_submitError != null) ...[
          Text(_submitError!, style: TextStyle(color: theme.colorScheme.error)),
          const SizedBox(height: 12),
        ],
        FilledButton(
          onPressed: _isSubmitting ? null : () => _submit(check.id),
          child: Text(_isSubmitting ? 'Submitting…' : 'Submit answer'),
        ),
        const SizedBox(height: 8),
        Text(
          'You can answer once. Facilities read your answer; nobody replies to it here.',
          style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline),
        ),
      ],
    );
  }
}

/// What was reported and what the technician says they did — the claim being checked. Both
/// verbatim and selectable: the note is evidence, and a note cut short can drop the clause
/// that matters.
class _Claim extends StatelessWidget {
  const _Claim({required this.check});

  final VerificationDetail check;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('What you reported', style: theme.textTheme.labelLarge),
        const SizedBox(height: 4),
        SelectableText(check.reportDescription, style: theme.textTheme.bodyLarge),
        const SizedBox(height: 4),
        Text('${check.assetName} · ${check.assetTag}', style: muted),
        const SizedBox(height: 20),
        Text('What the technician did', style: theme.textTheme.labelLarge),
        const SizedBox(height: 4),
        SelectableText(
          check.resolutionNote ?? 'No note was recorded for this repair.',
          style: theme.textTheme.bodyLarge,
        ),
        const SizedBox(height: 4),
        Text('Completed ${formatTimestamp(check.completedAt)}', style: muted),
      ],
    );
  }
}

/// Where the check has got to, in place of the form. What the answer did is C#; what the
/// automated review thinks is shown beside it as a review, never as the status.
class _StatusPanel extends StatelessWidget {
  const _StatusPanel({required this.check});

  final VerificationDetail check;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);
    final outcome = check.agentOutcome;

    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Text('Status', style: theme.textTheme.labelLarge),
                const SizedBox(width: 8),
                VerificationStatusChip(status: check.status),
              ],
            ),
            const SizedBox(height: 8),
            Text(describeForReporter(check.status), style: theme.textTheme.bodyMedium),
            if (check.status == VerificationStatuses.pending) ...[
              const SizedBox(height: 4),
              Text('We will ask from ${formatTimestamp(check.dueAt)}.', style: muted),
            ],
            if (check.isAnswered) ...[
              const SizedBox(height: 12),
              Text(
                // Null is "not answered", which isAnswered has already ruled out.
                'Your answer: ${check.reporterConfirmed == true ? "Yes, it's fixed" : 'No, still broken'}',
                style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
              ),
              Text(formatTimestamp(check.reporterRespondedAt), style: muted),
            ],
            if (outcome != null) ...[
              const SizedBox(height: 16),
              AgentOutcomeChip(outcome: outcome),
              const SizedBox(height: 6),
              Text(describeAgentOutcome(outcome), style: theme.textTheme.bodyMedium),
              if (check.agentReason != null) ...[
                const SizedBox(height: 4),
                SelectableText(check.agentReason!, style: muted),
              ],
            ] else if (check.agentQueuedAt != null) ...[
              const SizedBox(height: 16),
              Text(
                'Sent for review ${formatTimestamp(check.agentQueuedAt)}. If the review flags '
                'anything, it will show here and on your report.',
                style: theme.textTheme.bodyMedium,
              ),
            ],
            const SizedBox(height: 16),
            FilledButton.tonal(
              onPressed: () => context.go(MyReportsScreen.path),
              child: const Text('See it on my reports'),
            ),
          ],
        ),
      ),
    );
  }
}
