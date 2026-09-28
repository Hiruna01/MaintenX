import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../../widgets/surfaces.dart';
import 'asset.dart';
import 'asset_chips.dart';
import 'assets_api.dart';

String _messageFor(Object error) =>
    error is ApiException ? error.message : 'Could not reach the API.';

/// One asset: what it is, where it is, whether it is under warranty, and every service
/// visit it has had.
///
/// Two requests with their own states. The asset is the screen — if it fails, the screen
/// is an ErrorView. The failure summary only feeds the warranty chip and the repeat-failure
/// marker, so if it fails those say "unavailable" and the history, which is the evidence
/// the summary is computed from, still shows.
class AssetDetailScreen extends ConsumerWidget {
  const AssetDetailScreen({super.key, required this.assetId});

  /// Nested under home, like the report screen: `/assets/42`.
  static const String subPath = 'assets/:id';

  static String location(int id) => '/assets/$id';

  /// Null when the route carried something that is not an id — rendered as an error, not
  /// a crash.
  final int? assetId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final id = assetId;
    final appBar = AppBar(
      leading: Navigator.canPop(context)
          ? IconButton(
              tooltip: 'Back',
              icon: const Icon(LucideIcons.arrowLeft),
              onPressed: () => Navigator.maybePop(context),
            )
          : null,
    );
    if (id == null) {
      return Scaffold(
        appBar: appBar,
        body: const ErrorView(
          title: 'Not an asset',
          message: 'This link does not point at an asset.',
        ),
      );
    }

    final asset = ref.watch(assetDetailProvider(id));
    final summary = ref.watch(failureSummaryProvider(id));

    void refresh() {
      ref.invalidate(assetDetailProvider(id));
      ref.invalidate(failureSummaryProvider(id));
    }

    return Scaffold(
      appBar: appBar,
      body: SafeArea(
        top: false,
        // All three states of the asset request are rendered explicitly.
        child: asset.when(
          loading: () => const LoadingView(message: 'Loading asset…'),
          error: (error, _) => ErrorView(
            title: error is ApiException && error.statusCode == 404
                ? 'Asset not found'
                : 'Could not load this asset',
            message: error is ApiException && error.statusCode == 404
                ? 'There is no asset with this id in the registry.'
                : _messageFor(error),
            onRetry: refresh,
          ),
          data: (data) => RefreshIndicator(
            color: MxColors.ink,
            backgroundColor: MxColors.surface,
            onRefresh: () async {
              refresh();
              await ref.read(assetDetailProvider(id).future);
            },
            child: _AssetBody(asset: data, summary: summary, onRetrySummary: refresh),
          ),
        ),
      ),
    );
  }
}

class _AssetBody extends StatelessWidget {
  const _AssetBody({required this.asset, required this.summary, required this.onRetrySummary});

  final AssetDetail asset;
  final AsyncValue<FailureSummary> summary;
  final VoidCallback onRetrySummary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final history = asset.serviceHistory;

    return ListView(
      // Always scrollable, so pull-to-refresh works on a short page too.
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 32),
      children: [
        _Header(asset: asset, summary: summary),
        const SizedBox(height: 18),
        _FactsCard(asset: asset),
        const SizedBox(height: 12),
        _SummaryCard(summary: summary, onRetry: onRetrySummary),
        const SizedBox(height: 28),
        Text('Service history', style: theme.textTheme.titleLarge),
        const SizedBox(height: 2),
        Text(
          'Oldest first, each note exactly as the technician wrote it.',
          style: theme.textTheme.bodySmall,
        ),
        const SizedBox(height: 16),
        // An empty history is not a failure and must not look like one.
        if (history.isEmpty)
          const SizedBox(
            height: 200,
            child: EmptyView(message: 'No service visits on record.', icon: LucideIcons.history),
          )
        else
          // Rendered in the order the API sent it — oldest first. A repeat failure only
          // reads as one in the order it happened, so nothing here re-sorts.
          for (var i = 0; i < history.length; i++)
            _ServiceRecordEntry(
              record: history[i],
              index: i,
              total: history.length,
              isLast: i == history.length - 1,
            ),
      ],
    );
  }
}

/// What the machine is, and the two answers a reader wants first: is it working, is it
/// covered.
class _Header extends StatelessWidget {
  const _Header({required this.asset, required this.summary});

  final AssetDetail asset;
  final AsyncValue<FailureSummary> summary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            AssetStatusChip(status: asset.status),
            WarrantyChip(
              // The API's answer, never a date comparison made here.
              isUnderWarranty: summary.valueOrNull?.isUnderWarranty,
              warrantyExpiresOn: asset.warrantyExpiresOn,
              isLoading: summary.isLoading,
            ),
          ],
        ),
        const SizedBox(height: 14),
        Text(asset.name, style: theme.textTheme.headlineMedium),
        const SizedBox(height: 4),
        Text(
          [asset.categoryName, asset.makeAndModel].whereType<String>().join(', '),
          style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
        ),
      ],
    );
  }
}

/// The registry's facts, two by two.
class _FactsCard extends StatelessWidget {
  const _FactsCard({required this.asset});

  final AssetDetail asset;

  @override
  Widget build(BuildContext context) {
    return MxCard(
      padding: const EdgeInsets.fromLTRB(18, 16, 18, 16),
      child: Column(
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _Fact(label: 'Asset tag', value: asset.assetTag, icon: LucideIcons.qrCode),
              _Fact(
                label: 'Room',
                value: '${asset.room.label} (floor ${asset.room.floor})',
                icon: LucideIcons.mapPin,
              ),
            ],
          ),
          const SizedBox(height: 12),
          const Divider(),
          const SizedBox(height: 12),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _Fact(
                label: 'Installed',
                value: formatDateOnly(asset.installedOn),
                icon: LucideIcons.calendar,
              ),
              _Fact(
                label: 'Warranty until',
                value: asset.warrantyExpiresOn == null
                    ? 'Not recorded'
                    : formatDateOnly(asset.warrantyExpiresOn),
                icon: LucideIcons.shieldCheck,
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact({required this.label, required this.value, required this.icon});

  final String label;
  final String value;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Expanded(
      child: Padding(
        padding: const EdgeInsets.only(right: 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            MxPanelLabel(label, icon: icon),
            const SizedBox(height: 4),
            Text(
              value,
              style: theme.textTheme.titleSmall?.copyWith(
                fontSize: 15,
                fontFeatures: MxType.tabular,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The failure summary, exactly as the API computed it: counts and date comparisons in
/// C#, never recomputed here.
class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.summary, required this.onRetry});

  final AsyncValue<FailureSummary> summary;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final meta = theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular);

    return MxCard(
      child: summary.when(
        loading: () => Row(
          children: [
            const SizedBox(
              width: 16,
              height: 16,
              child: CircularProgressIndicator(strokeWidth: 2, strokeCap: StrokeCap.round),
            ),
            const SizedBox(width: 12),
            Text('Loading failure summary…', style: meta),
          ],
        ),
        error: (error, _) => Row(
          children: [
            const Icon(LucideIcons.circleAlert, size: 18, color: MxColors.red),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                'Failure summary unavailable. ${_messageFor(error)}',
                style: theme.textTheme.bodyMedium,
              ),
            ),
            TextButton(onPressed: onRetry, child: const Text('Retry')),
          ],
        ),
        data: (data) => Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (data.isRepeatFailure) ...[
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: MxColors.redBg,
                  borderRadius: BorderRadius.circular(MxRadii.md),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const RepeatFailureChip(),
                    const SizedBox(height: 8),
                    Text(
                      'Three or more service visits in the last 90 days. The pattern is in the '
                      'notes below, not on the asset record.',
                      style: theme.textTheme.bodySmall?.copyWith(color: MxColors.red),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 16),
            ],
            IntrinsicHeight(
              child: Row(
                children: [
                  _Stat(label: 'Last 90 days', value: '${data.failureCount3Months}'),
                  const VerticalDivider(width: 24),
                  _Stat(label: 'Last 12 months', value: '${data.failureCount12Months}'),
                  const VerticalDivider(width: 24),
                  _Stat(label: 'Temporary fixes', value: '${data.temporaryFixCount}'),
                ],
              ),
            ),
            const SizedBox(height: 14),
            const Divider(),
            const SizedBox(height: 12),
            Row(
              children: [
                const Icon(LucideIcons.wrench, size: 14, color: MxColors.graphite),
                const SizedBox(width: 6),
                Expanded(
                  child: Text(
                    // Null is not zero: a machine nobody has touched was not serviced today.
                    data.lastServicedOn == null
                        ? 'Never serviced.'
                        : 'Last serviced ${formatDateOnly(data.lastServicedOn)} — '
                            '${data.daysSinceLastService} '
                            '${data.daysSinceLastService == 1 ? 'day' : 'days'} ago.',
                    style: meta,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            value,
            style: theme.textTheme.headlineMedium?.copyWith(fontFeatures: MxType.tabular),
          ),
          const SizedBox(height: 2),
          Text(label, style: theme.textTheme.bodySmall),
        ],
      ),
    );
  }
}

/// One visit on the timeline: a dot in the outcome's colour on a rail joining the visits in
/// the order they happened, and a card beside it. The technician's note is shown VERBATIM —
/// no maxLines, no ellipsis, no "read more" — because the fault this history is evidence of
/// is spread across several terse notes, and a note cut to its first line can drop exactly
/// the clause that matters.
class _ServiceRecordEntry extends StatelessWidget {
  const _ServiceRecordEntry({
    required this.record,
    required this.index,
    required this.total,
    required this.isLast,
  });

  final ServiceRecord record;
  final int index;
  final int total;
  final bool isLast;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final accent = outcomeColor(record.outcome);
    final meta = theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular);

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: 22,
            child: Column(
              children: [
                const SizedBox(height: 20),
                Container(
                  width: 12,
                  height: 12,
                  decoration: BoxDecoration(
                    color: accent,
                    shape: BoxShape.circle,
                    border: Border.all(color: MxColors.canvas, width: 2),
                  ),
                ),
                if (!isLast)
                  Expanded(child: Container(width: 2, color: MxColors.hairline))
                else
                  const Spacer(),
              ],
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: MxCard(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            formatDateOnly(record.servicedOn),
                            style: theme.textTheme.titleMedium?.copyWith(
                              fontFeatures: MxType.tabular,
                            ),
                          ),
                        ),
                        Text('Visit ${index + 1} of $total', style: meta),
                      ],
                    ),
                    const SizedBox(height: 8),
                    OutcomeChip(outcome: record.outcome),
                    const SizedBox(height: 12),
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
                      decoration: BoxDecoration(
                        color: MxColors.well,
                        borderRadius: BorderRadius.circular(MxRadii.sm),
                        border: Border(left: BorderSide(color: accent, width: 3)),
                      ),
                      child: record.technicianNote == null || record.technicianNote!.isEmpty
                          ? Text(
                              'No note recorded.',
                              style: meta?.copyWith(fontStyle: FontStyle.italic),
                            )
                          : SelectableText(
                              record.technicianNote!,
                              style: theme.textTheme.bodyMedium?.copyWith(height: 1.5),
                            ),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      [
                        record.technicianName,
                        if (record.workOrderId != null) 'Work order #${record.workOrderId}',
                      ].join(' · '),
                      style: meta,
                    ),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
