import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../core/paged_result.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/status_pill.dart';
import '../../widgets/surfaces.dart';
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
    final theme = Theme.of(context);
    final page = ref.watch(pendingVerificationsProvider(_page));

    return Scaffold(
      appBar: AppBar(
        leading: Navigator.canPop(context)
            ? IconButton(
                tooltip: 'Back',
                icon: const Icon(LucideIcons.arrowLeft),
                onPressed: () => Navigator.maybePop(context),
              )
            : null,
      ),
      body: SafeArea(
        top: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Confirm repairs', style: theme.textTheme.headlineMedium),
                  const SizedBox(height: 6),
                  Text(
                    'Tell facilities whether each repair has held. One answer per repair.',
                    style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
                  ),
                ],
              ),
            ),
            Expanded(
              child: page.when(
                loading: () => const _CheckListSkeleton(),
                error: (error, _) => ErrorView(
                  title: 'Could not load your repairs',
                  message: error is ApiException ? error.message : 'Could not reach the API.',
                  onRetry: () => ref.invalidate(pendingVerificationsProvider(_page)),
                ),
                data: (result) => RefreshIndicator(
                  color: MxColors.ink,
                  backgroundColor: MxColors.surface,
                  onRefresh: () => ref.refresh(pendingVerificationsProvider(_page).future),
                  child: result.items.isEmpty ? _empty() : _list(result),
                ),
              ),
            ),
          ],
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
            icon: LucideIcons.clipboardCheck,
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
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
      children: [
        for (final check in result.items) ...[
          _CheckCard(check: check),
          const SizedBox(height: 12),
        ],
        if (result.totalPages > 1)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: MxPager(
              page: result.page,
              totalPages: result.totalPages,
              caption: '${result.totalCount} repairs',
              onPrevious: result.hasPrevious ? () => setState(() => _page--) : null,
              onNext: result.hasNext ? () => setState(() => _page++) : null,
            ),
          ),
      ],
    );
  }
}

/// One repair waiting on its reporter. Leads with what THEY reported — a reporter does not
/// know the asset tag — and ends in the iris band every "waiting on you" row in the app
/// ends in.
class _CheckCard extends StatelessWidget {
  const _CheckCard({required this.check});

  final VerificationListItem check;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return MxCard(
      padding: EdgeInsets.zero,
      onTap: () => context.go(ConfirmFixScreen.location(check.id)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(18, 16, 18, 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
                      decoration: BoxDecoration(
                        color: MxColors.well,
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(
                        check.assetTag,
                        style: theme.textTheme.labelSmall?.copyWith(
                          fontWeight: FontWeight.w600,
                          fontFeatures: MxType.tabular,
                        ),
                      ),
                    ),
                    const Spacer(),
                    // The API's flag, coloured. No date is compared on the phone.
                    if (check.isOverdue)
                      const StatusPill(
                        label: 'Waiting a while',
                        tone: PillTone.warn,
                        icon: LucideIcons.clock,
                      ),
                  ],
                ),
                const SizedBox(height: 12),
                Text(
                  check.reportDescription,
                  maxLines: 3,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyLarge?.copyWith(
                    fontWeight: FontWeight.w500,
                    height: 1.4,
                  ),
                ),
                const SizedBox(height: 10),
                Row(
                  children: [
                    const Icon(LucideIcons.wrench, size: 14, color: MxColors.graphite),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        'Repaired ${formatTimestamp(check.workOrderCompletedAt)}',
                        style: theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
          Container(
            color: MxColors.irisSoft,
            padding: const EdgeInsets.fromLTRB(18, 12, 12, 12),
            child: Row(
              children: [
                const Icon(LucideIcons.circleHelp, size: 18, color: MxColors.iris),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    'Is this fixed?',
                    style: theme.textTheme.titleSmall?.copyWith(color: MxColors.iris),
                  ),
                ),
                Text(
                  'Answer',
                  style: theme.textTheme.labelLarge?.copyWith(color: MxColors.iris),
                ),
                const Icon(LucideIcons.chevronRight, size: 18, color: MxColors.iris),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Loading, shaped like the cards it stands in for.
class _CheckListSkeleton extends StatelessWidget {
  const _CheckListSkeleton();

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'Checking for repairs to confirm…',
      child: ListView(
        physics: const NeverScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
        children: [
          for (var i = 0; i < 2; i++) ...[
            const MxCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Skeleton(width: 110, height: 22),
                  SizedBox(height: 16),
                  Skeleton(height: 14),
                  SizedBox(height: 8),
                  Skeleton(width: 220, height: 14),
                  SizedBox(height: 14),
                  Skeleton(width: 150, height: 12),
                ],
              ),
            ),
            const SizedBox(height: 12),
          ],
        ],
      ),
    );
  }
}
