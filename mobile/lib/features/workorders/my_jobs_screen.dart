import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../core/paged_result.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
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
    return Scaffold(
      appBar: AppBar(title: const Text('My jobs')),
      body: SafeArea(
        child: Column(
          children: [
            _StatusFilter(selected: _status, onSelected: _setStatus),
            const Divider(height: 1),
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
      loading: () => const LoadingView(message: 'Loading your jobs…'),
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
                  icon: Icons.assignment_turned_in_outlined,
                  message: 'No jobs are assigned to you right now.\n'
                      'New work appears here once a facilities manager assigns it.',
                )
              : EmptyView(
                  icon: Icons.filter_alt_off_outlined,
                  message: 'None of your jobs are '
                      '${WorkOrderStatuses.label(_status!).toLowerCase()}.',
                  action: TextButton(
                    onPressed: () => _setStatus(null),
                    child: const Text('Show all jobs'),
                  ),
                );
        }
        return RefreshIndicator(
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
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
      children: [
        for (final job in result.items) _JobCard(job: job),
        if (result.totalPages > 1)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Row(
              children: [
                IconButton(
                  tooltip: 'Previous page',
                  icon: const Icon(Icons.chevron_left),
                  onPressed: result.hasPrevious ? () => setState(() => _page--) : null,
                ),
                Expanded(
                  child: Text(
                    'Page ${result.page} of ${result.totalPages} · ${result.totalCount} jobs',
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
      height: 52,
      child: ListView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
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

class _JobCard extends StatelessWidget {
  const _JobCard({required this.job});

  final WorkOrderListItem job;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.go(JobDetailScreen.location(job.id)),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  WorkOrderStatusChip(status: job.status),
                  const Spacer(),
                  Text('#${job.id}', style: muted),
                ],
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  Icon(Icons.qr_code_2, size: 18, color: theme.colorScheme.outline),
                  const SizedBox(width: 6),
                  Expanded(child: Text(job.assetTag, style: theme.textTheme.titleMedium)),
                  const Icon(Icons.chevron_right),
                ],
              ),
              const SizedBox(height: 4),
              Text(WorkOrderStrategies.label(job.strategy), style: theme.textTheme.bodyMedium),
              const SizedBox(height: 4),
              Text(
                job.completedAt != null
                    ? 'Completed ${formatTimestamp(job.completedAt)}'
                    : 'Raised ${formatTimestamp(job.createdAt)}',
                style: muted,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
