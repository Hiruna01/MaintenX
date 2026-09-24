import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../core/paged_result.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../../widgets/status_pill.dart';
import '../reports/report.dart' show formatTimestamp;
import 'confirm_fix_screen.dart';
import 'verification.dart';
import 'verification_api.dart';

/// Repairs waiting on this reporter's answer: "is the problem fixed?"
///
/// "This reporter's" is the API's decision, not this screen's — GET /api/verifications
/// scopes a Reporter to checks on the reports they filed, from the token. The status filter
/// is fixed to AwaitingReporterResponse, by NAME, because that is the only state the API
/// accepts an answer in; a check still waiting out its delay has nothing to ask yet.
///
/// AN EMPTY LIST IS THE NORMAL CASE. Most days nobody is being asked anything, so empty is an
/// EmptyView that says what will appear and when — never an ErrorView, and never a blank.
class PendingConfirmationsScreen extends ConsumerStatefulWidget {
  const PendingConfirmationsScreen({super.key});

  static const String subPath = 'verifications';
  static const String path = '/verifications';

  @override
  ConsumerState<PendingConfirmationsScreen> createState() => _PendingConfirmationsScreenState();
}

class _PendingConfirmationsScreenState extends ConsumerState<PendingConfirmationsScreen> {
  int _page = 1;

  @override
  Widget build(BuildContext context) {
    final page = ref.watch(pendingVerificationsProvider(_page));

    return Scaffold(
      appBar: AppBar(title: const Text('Confirm repairs')),
      body: SafeArea(
        child: page.when(
          loading: () => const LoadingView(message: 'Checking for repairs to confirm…'),
          error: (error, _) => ErrorView(
            title: 'Could not load your repairs',
            message: error is ApiException ? error.message : 'Could not reach the API.',
            onRetry: () => ref.invalidate(pendingVerificationsProvider(_page)),
          ),
          data: (result) => RefreshIndicator(
            onRefresh: () => ref.refresh(pendingVerificationsProvider(_page).future),
            child: result.items.isEmpty ? _empty() : _list(result),
          ),
        ),
      ),
    );
  }

  /// Still scrollable, so pull-to-refresh works on it — which is how a reporter checks
  /// whether anything has come in.
  Widget _empty() {
    return LayoutBuilder(
      builder: (context, constraints) => SingleChildScrollView(
        physics: const AlwaysScrollableScrollPhysics(),
        child: SizedBox(
          height: constraints.maxHeight,
          child: const EmptyView(
            icon: Icons.task_alt,
            message: 'Nothing to confirm right now.\n'
                'A few days after something you reported is repaired, '
                'we will ask you here whether the repair held.',
          ),
        ),
      ),
    );
  }

  Widget _list(PagedResult<VerificationListItem> result) {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
      children: [
        for (final check in result.items) _CheckCard(check: check),
        if (result.totalPages > 1)
          Row(
            children: [
              IconButton(
                tooltip: 'Previous page',
                icon: const Icon(Icons.chevron_left),
                onPressed: result.hasPrevious ? () => setState(() => _page--) : null,
              ),
              Expanded(
                child: Text(
                  'Page ${result.page} of ${result.totalPages} · ${result.totalCount} repairs',
                  textAlign: TextAlign.center,
                ),
              ),
              IconButton(
                tooltip: 'Next page',
                icon: const Icon(Icons.chevron_right),
                onPressed: result.hasNext ? () => setState(() => _page++) : null,
              ),
            ],
          ),
      ],
    );
  }
}

class _CheckCard extends StatelessWidget {
  const _CheckCard({required this.check});

  final VerificationListItem check;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.go(ConfirmFixScreen.location(check.id)),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text('Is this fixed?', style: theme.textTheme.titleSmall),
                  const Spacer(),
                  // The API's flag, coloured. No date is compared on the phone.
                  if (check.isOverdue)
                    const StatusPill(
                      label: 'Waiting a while',
                      tone: PillTone.warn,
                      icon: Icons.schedule,
                    ),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                check.reportDescription,
                maxLines: 3,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.bodyLarge,
              ),
              const SizedBox(height: 6),
              Text(
                '${check.assetTag} · repaired ${formatTimestamp(check.workOrderCompletedAt)}',
                style: muted,
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      'Answer',
                      style: TextStyle(
                        color: theme.colorScheme.primary,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                  Icon(Icons.chevron_right, color: theme.colorScheme.primary),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
