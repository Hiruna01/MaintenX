import 'dart:async';

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
import '../verification/confirm_fix_screen.dart';
import '../verification/verification_status_chip.dart';
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
        actions: [
          MxRoundButton(
            tooltip: 'Submit a report',
            icon: LucideIcons.plus,
            background: MxColors.ink,
            foreground: Colors.white,
            onPressed: () => context.go(SubmitReportScreen.path),
          ),
          const SizedBox(width: 12),
        ],
      ),
      body: SafeArea(
        top: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 14),
              child: Text('My reports', style: theme.textTheme.headlineMedium),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: TextField(
                controller: _searchController,
                onChanged: _onSearchChanged,
                textInputAction: TextInputAction.search,
                decoration: InputDecoration(
                  hintText: 'Search descriptions',
                  fillColor: MxColors.surface,
                  prefixIcon: const Icon(LucideIcons.search, size: 18),
                  contentPadding: const EdgeInsets.symmetric(vertical: 14),
                  border: const OutlineInputBorder(
                    borderRadius: BorderRadius.all(Radius.circular(999)),
                    borderSide: BorderSide.none,
                  ),
                  enabledBorder: const OutlineInputBorder(
                    borderRadius: BorderRadius.all(Radius.circular(999)),
                    borderSide: BorderSide.none,
                  ),
                  focusedBorder: const OutlineInputBorder(
                    borderRadius: BorderRadius.all(Radius.circular(999)),
                    borderSide: BorderSide(color: MxColors.iris, width: 1.6),
                  ),
                  suffixIcon: ValueListenableBuilder(
                    valueListenable: _searchController,
                    builder: (context, value, _) => value.text.isEmpty
                        ? const SizedBox.shrink()
                        : IconButton(
                            tooltip: 'Clear search',
                            icon: const Icon(LucideIcons.x, size: 18),
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
      loading: () => const _ReportListSkeleton(),
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
                  icon: LucideIcons.searchX,
                  message: 'No reports match your search or filter.',
                  action: OutlinedButton(
                    onPressed: _clearFilters,
                    child: const Text('Clear filters'),
                  ),
                )
              : EmptyView(
                  icon: LucideIcons.fileText,
                  message: 'You have not reported anything yet.\n'
                      'Reports you submit will appear here.',
                  action: FilledButton(
                    onPressed: () => context.go(SubmitReportScreen.path),
                    child: const Text('Submit a report'),
                  ),
                );
        }
        return RefreshIndicator(
          color: MxColors.ink,
          backgroundColor: MxColors.surface,
          onRefresh: () => ref.refresh(reportsPageProvider(_query).future),
          child: _list(result),
        );
      },
    );
  }

  Widget _list(PagedResult<ReportListItem> result) {
    final theme = Theme.of(context);
    return ListView(
      // Always scrollable, so pull-to-refresh works on a short list — which is how a
      // reporter checks whether the agent has asked them anything yet.
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
      children: [
        for (final report in result.items) ...[
          _ReportCard(report: report),
          const SizedBox(height: 12),
        ],
        if (result.totalPages > 1)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Row(
              children: [
                MxRoundButton(
                  tooltip: 'Previous page',
                  icon: LucideIcons.chevronLeft,
                  onPressed: result.hasPrevious ? () => setState(() => _page--) : null,
                ),
                Expanded(
                  child: Column(
                    children: [
                      Text(
                        'Page ${result.page} of ${result.totalPages}',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.titleSmall?.copyWith(
                          fontFeatures: MxType.tabular,
                        ),
                      ),
                      Text(
                        '${result.totalCount} reports',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.bodySmall?.copyWith(
                          fontFeatures: MxType.tabular,
                        ),
                      ),
                    ],
                  ),
                ),
                MxRoundButton(
                  tooltip: 'Next page',
                  icon: LucideIcons.chevronRight,
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
    final waiting = report.isWaitingOnReporter;
    final count = report.unansweredQuestionCount;
    final verification = report.verification;

    return MxCard(
      padding: EdgeInsets.zero,
      // Only a report with something behind it goes anywhere: open questions, or a repair
      // check — which is either a question waiting on the reporter or the status of what
      // their answer did. Every other row is a status to read, and a tap that led to an
      // empty page would be worse than no tap. Pushed rather than gone to, so back comes
      // here and not to the pending list the check screen is nested under.
      onTap: waiting
          ? () => context.go(ClarificationScreen.location(report.id))
          : verification != null
              ? () => context.push(ConfirmFixScreen.location(verification.id))
              : null,
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
                    ReportStatusChip(status: report.status),
                    const Spacer(),
                    Text(
                      formatTimestamp(report.createdAt),
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: MxColors.mute,
                        fontFeatures: MxType.tabular,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Text(
                  report.description,
                  maxLines: 3,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyLarge?.copyWith(
                    fontWeight: FontWeight.w500,
                    height: 1.4,
                    letterSpacing: -0.1,
                  ),
                ),
                const SizedBox(height: 10),
                Row(
                  children: [
                    const Icon(LucideIcons.mapPin, size: 14, color: MxColors.graphite),
                    const SizedBox(width: 6),
                    Expanded(child: Text(report.roomName, style: theme.textTheme.bodySmall)),
                  ],
                ),
                if (verification != null) ...[
                  const SizedBox(height: 14),
                  MxWell(
                    padding: const EdgeInsets.fromLTRB(14, 12, 10, 12),
                    child: ReportVerificationLine(verification: verification),
                  ),
                ],
              ],
            ),
          ),
          // The one thing in iris on this screen: this report is waiting on its reader.
          if (waiting)
            Container(
              color: MxColors.irisSoft,
              padding: const EdgeInsets.fromLTRB(18, 12, 12, 12),
              child: Row(
                children: [
                  const Icon(LucideIcons.messageCircleQuestion, size: 18, color: MxColors.iris),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      '$count ${count == 1 ? 'question' : 'questions'} waiting on you',
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
class _ReportListSkeleton extends StatelessWidget {
  const _ReportListSkeleton();

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'Loading your reports…',
      child: ListView(
        physics: const NeverScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
        children: [
          for (var i = 0; i < 3; i++) ...[
            const MxCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Skeleton(width: 96, height: 22, radius: 99),
                      Spacer(),
                      Skeleton(width: 110, height: 12),
                    ],
                  ),
                  SizedBox(height: 16),
                  Skeleton(height: 14),
                  SizedBox(height: 8),
                  Skeleton(width: 200, height: 14),
                  SizedBox(height: 14),
                  Skeleton(width: 120, height: 12),
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
