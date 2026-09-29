import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../core/paged_result.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/surfaces.dart';
import '../reports/report.dart' show formatTimestamp;
import 'job_detail_screen.dart';
import 'work_order.dart';
import 'work_order_status_chip.dart';
import 'work_orders_api.dart';

/// The signed-in technician's jobs: the work orders assigned to them, newest first.
///
/// "Assigned to them" is the API's decision, not this screen's. GET /api/workorders scopes
/// a Technician to their own orders from the token's role, and there is no parameter here
/// that widens it. The status filter is SERVER-SIDE, by enum NAME, so it holds across every
/// page rather than only the one fetched.
///
/// An empty list is normal — a technician with nothing assigned today — and is an
/// EmptyView, never an error.
class MyJobsScreen extends ConsumerStatefulWidget {
  const MyJobsScreen({super.key});

  static const String subPath = 'jobs';
  static const String path = '/jobs';

  @override
  ConsumerState<MyJobsScreen> createState() => _MyJobsScreenState();
}

class _MyJobsScreenState extends ConsumerState<MyJobsScreen> {
  String? _status;
  int _page = 1;

  JobsQuery get _query => (status: _status, page: _page);

  void _setStatus(String? status) {
    setState(() {
      _status = status;
      // A new filter starts at the top; page 3 of the old results means nothing.
      _page = 1;
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

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
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 2),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('My jobs', style: theme.textTheme.headlineMedium),
                  const SizedBox(height: 6),
                  Text(
                    'Work orders assigned to you, newest first.',
                    style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
                  ),
                ],
              ),
            ),
            _StatusFilter(selected: _status, onSelected: _setStatus),
            // Only the list switches state; the filter stays put, so a failed or empty
            // result can be filtered out of.
            Expanded(child: _results()),
          ],
        ),
      ),
    );
  }

  Widget _results() {
    final page = ref.watch(jobsPageProvider(_query));

    return page.when(
      loading: () => const _JobListSkeleton(),
      error: (error, _) => ErrorView(
        title: 'Could not load your jobs',
        message: error is ApiException ? error.message : 'Could not reach the API.',
        onRetry: () => ref.invalidate(jobsPageProvider(_query)),
      ),
      data: (result) {
        if (result.items.isEmpty) {
          // Two different empties, neither of them a failure.
          return _status == null
              ? const EmptyView(
                  icon: LucideIcons.clipboardList,
                  message: 'No jobs are assigned to you right now.\n'
                      'New work appears here once a facilities manager assigns it.',
                )
              : EmptyView(
                  icon: LucideIcons.listFilter,
                  message: 'None of your jobs are '
                      '${WorkOrderStatuses.label(_status!).toLowerCase()}.',
                  action: OutlinedButton(
                    onPressed: () => _setStatus(null),
                    child: const Text('Show all jobs'),
                  ),
                );
        }
        return RefreshIndicator(
          color: MxColors.ink,
          backgroundColor: MxColors.surface,
          onRefresh: () => ref.refresh(jobsPageProvider(_query).future),
          child: _list(result),
        );
      },
    );
  }

  Widget _list(PagedResult<WorkOrderListItem> result) {
    return ListView(
      // Always scrollable, so pull-to-refresh works on a short list.
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
      children: [
        for (final job in result.items) ...[
          _JobCard(job: job),
          const SizedBox(height: 12),
        ],
        if (result.totalPages > 1)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: MxPager(
              page: result.page,
              totalPages: result.totalPages,
              caption: '${result.totalCount} jobs',
              onPrevious: result.hasPrevious ? () => setState(() => _page--) : null,
              onNext: result.hasNext ? () => setState(() => _page++) : null,
            ),
          ),
      ],
    );
  }
}

/// "All" plus one chip per status a technician's job can actually be in, by NAME.
class _StatusFilter extends StatelessWidget {
  const _StatusFilter({required this.selected, required this.onSelected});

  final String? selected;
  final ValueChanged<String?> onSelected;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 64,
      child: ListView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 8),
        children: [
          ChoiceChip(
            label: const Text('All'),
            selected: selected == null,
            onSelected: (_) => onSelected(null),
          ),
          for (final status in WorkOrderStatuses.technicianFilters) ...[
            const SizedBox(width: 8),
            ChoiceChip(
              label: Text(WorkOrderStatuses.label(status)),
              selected: selected == status,
              // Tapping the selected chip again goes back to "All".
              onSelected: (isSelected) => onSelected(isSelected ? status : null),
            ),
          ],
        ],
      ),
    );
  }
}

/// One job: the machine it is for, leading, because that is what a technician walks up to.
class _JobCard extends StatelessWidget {
  const _JobCard({required this.job});

  final WorkOrderListItem job;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final meta = theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular);
    final done = job.completedAt != null;

    return MxCard(
      onTap: () => context.go(JobDetailScreen.location(job.id)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              WorkOrderStatusChip(status: job.status),
              const Spacer(),
              Text('#${job.id}', style: meta?.copyWith(color: MxColors.mute)),
            ],
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              const MxIconTile(icon: LucideIcons.qrCode, size: 44),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      job.assetTag,
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontSize: 17,
                        fontFeatures: MxType.tabular,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      WorkOrderStrategies.label(job.strategy),
                      style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
                    ),
                  ],
                ),
              ),
              const Icon(LucideIcons.chevronRight, size: 18, color: MxColors.mute),
            ],
          ),
          const SizedBox(height: 14),
          const Divider(),
          const SizedBox(height: 12),
          Row(
            children: [
              Icon(
                done ? LucideIcons.circleCheck : LucideIcons.clock,
                size: 14,
                color: MxColors.graphite,
              ),
              const SizedBox(width: 6),
              Text(
                done
                    ? 'Completed ${formatTimestamp(job.completedAt)}'
                    : 'Raised ${formatTimestamp(job.createdAt)}',
                style: meta,
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Loading, shaped like the cards it stands in for.
class _JobListSkeleton extends StatelessWidget {
  const _JobListSkeleton();

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'Loading your jobs…',
      child: ListView(
        physics: const NeverScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
        children: [
          for (var i = 0; i < 3; i++) ...[
            const MxCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Skeleton(width: 90, height: 22, radius: 99),
                  SizedBox(height: 16),
                  Row(
                    children: [
                      Skeleton(width: 44, height: 44, radius: 14),
                      SizedBox(width: 14),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Skeleton(width: 140, height: 16),
                            SizedBox(height: 8),
                            Skeleton(width: 90, height: 12),
                          ],
                        ),
                      ),
                    ],
                  ),
                  SizedBox(height: 18),
                  Skeleton(width: 160, height: 12),
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
