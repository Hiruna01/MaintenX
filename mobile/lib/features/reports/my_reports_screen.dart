import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../core/paged_result.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import 'clarification_screen.dart';
import 'report.dart';
import 'report_status_chip.dart';
import 'reports_api.dart';
import 'submit_report_screen.dart';

/// The reporter's own reports: where each fault has got to, and which ones are waiting on
/// an answer from them.
///
/// "Own" is the API's decision, not this screen's. GET /api/reports scopes a Reporter to
/// the reports they filed from the token's role, and there is no parameter here — or
/// anywhere — that widens it.
///
/// The search and status filter are SERVER-SIDE: both go into the query string, so they
/// match across every page rather than only the one already fetched.
class MyReportsScreen extends ConsumerStatefulWidget {
  const MyReportsScreen({super.key});

  static const String subPath = 'reports';
  static const String path = '/reports';

  @override
  ConsumerState<MyReportsScreen> createState() => _MyReportsScreenState();
}

class _MyReportsScreenState extends ConsumerState<MyReportsScreen> {
  /// Long enough that typing a word is one request, not one per letter.
  static const Duration _debounce = Duration(milliseconds: 350);

  final _searchController = TextEditingController();
  Timer? _debounceTimer;

  String _search = '';
  String? _status;
  int _page = 1;

  ReportQuery get _query => (search: _search, status: _status, page: _page);

  bool get _isFiltered => _search.isNotEmpty || _status != null;

  @override
  void dispose() {
    _debounceTimer?.cancel();
    _searchController.dispose();
    super.dispose();
  }

  void _onSearchChanged(String value) {
    _debounceTimer?.cancel();
    _debounceTimer = Timer(_debounce, () {
      if (!mounted) return;
      setState(() {
        _search = value.trim();
        // A new search starts at the top; page 3 of the old results means nothing.
        _page = 1;
      });
    });
  }

  void _setStatus(String? status) {
    setState(() {
      _status = status;
      _page = 1;
    });
  }

  void _clearFilters() {
    _debounceTimer?.cancel();
    _searchController.clear();
    setState(() {
      _search = '';
      _status = null;
      _page = 1;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('My reports'),
        actions: [
          IconButton(
            tooltip: 'Submit a report',
            icon: const Icon(Icons.add),
            onPressed: () => context.go(SubmitReportScreen.path),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: TextField(
                controller: _searchController,
                onChanged: _onSearchChanged,
                textInputAction: TextInputAction.search,
                decoration: InputDecoration(
                  hintText: 'Search descriptions',
                  prefixIcon: const Icon(Icons.search),
                  border: const OutlineInputBorder(),
                  isDense: true,
                  suffixIcon: ValueListenableBuilder(
                    valueListenable: _searchController,
                    builder: (context, value, _) => value.text.isEmpty
                        ? const SizedBox.shrink()
                        : IconButton(
                            tooltip: 'Clear search',
                            icon: const Icon(Icons.clear),
                            onPressed: () {
                              _searchController.clear();
                              _onSearchChanged('');
                            },
                          ),
                  ),
                ),
              ),
            ),
            _StatusFilter(selected: _status, onSelected: _setStatus),
            const Divider(height: 1),
            // Only the list switches state; the search box and the filter stay put, so a
            // failed or empty result can be searched out of.
            Expanded(child: _results()),
          ],
        ),
      ),
    );
  }

  Widget _results() {
    final page = ref.watch(reportsPageProvider(_query));

    return page.when(
      loading: () => const LoadingView(message: 'Loading your reports…'),
      error: (error, _) => ErrorView(
        title: 'Could not load your reports',
        message: error is ApiException ? error.message : 'Could not reach the API.',
        onRetry: () => ref.invalidate(reportsPageProvider(_query)),
      ),
      data: (result) {
        // Empty is an ordinary answer, not a failure — and "nothing matches" is a
        // different answer from "you have not reported anything".
        if (result.items.isEmpty) {
          return _isFiltered
              ? EmptyView(
                  icon: Icons.search_off,
                  message: 'No reports match your search or filter.',
                  action: TextButton(
                    onPressed: _clearFilters,
                    child: const Text('Clear filters'),
                  ),
                )
              : EmptyView(
                  message: 'You have not reported anything yet.\n'
                      'Reports you submit will appear here.',
                  action: FilledButton.tonal(
                    onPressed: () => context.go(SubmitReportScreen.path),
                    child: const Text('Submit a report'),
                  ),
                );
        }
        return RefreshIndicator(
          onRefresh: () => ref.refresh(reportsPageProvider(_query).future),
          child: _list(result),
        );
      },
    );
  }

  Widget _list(PagedResult<ReportListItem> result) {
    return ListView(
      // Always scrollable, so pull-to-refresh works on a short list — which is how a
      // reporter checks whether the agent has asked them anything yet.
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
      children: [
        for (final report in result.items) _ReportCard(report: report),
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
                    'Page ${result.page} of ${result.totalPages} · '
                    '${result.totalCount} reports',
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

/// "All" plus one chip per `ReportStatus`, by NAME.
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
          for (final status in ReportStatuses.all) ...[
            const SizedBox(width: 8),
            ChoiceChip(
              label: Text(ReportStatuses.label(status)),
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

class _ReportCard extends StatelessWidget {
  const _ReportCard({required this.report});

  final ReportListItem report;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);
    final waiting = report.isWaitingOnReporter;
    final count = report.unansweredQuestionCount;

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        // Only a report with questions open goes anywhere. Every other row is a status to
        // read, and a tap that led to an empty page would be worse than no tap.
        onTap: waiting ? () => context.go(ClarificationScreen.location(report.id)) : null,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  ReportStatusChip(status: report.status),
                  const Spacer(),
                  Text(formatTimestamp(report.createdAt), style: muted),
                ],
              ),
              const SizedBox(height: 10),
              Text(
                report.description,
                maxLines: 3,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.bodyLarge,
              ),
              const SizedBox(height: 6),
              Row(
                children: [
                  Icon(Icons.place_outlined, size: 16, color: theme.colorScheme.outline),
                  const SizedBox(width: 4),
                  Expanded(child: Text(report.roomName, style: muted)),
                ],
              ),
              if (waiting) ...[
                const SizedBox(height: 10),
                Row(
                  children: [
                    Icon(Icons.help_outline, size: 18, color: theme.colorScheme.primary),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        '$count ${count == 1 ? 'question' : 'questions'} waiting on you',
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: theme.colorScheme.primary,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                    Text('Answer', style: TextStyle(color: theme.colorScheme.primary)),
                    Icon(Icons.chevron_right, color: theme.colorScheme.primary),
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
