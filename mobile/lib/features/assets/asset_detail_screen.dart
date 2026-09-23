import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
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
    if (id == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Asset')),
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
      appBar: AppBar(title: Text(asset.valueOrNull?.assetTag ?? 'Asset')),
      body: SafeArea(
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
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
      children: [
        _HeaderCard(asset: asset, summary: summary),
        const SizedBox(height: 12),
        _SummaryCard(summary: summary, onRetry: onRetrySummary),
        const SizedBox(height: 24),
        Text('Service history', style: theme.textTheme.titleMedium),
        const SizedBox(height: 2),
        Text(
          'Oldest first, each note exactly as the technician wrote it.',
          style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline),
        ),
        const SizedBox(height: 12),
        // An empty history is not a failure and must not look like one.
        if (history.isEmpty)
          const SizedBox(
            height: 180,
            child: EmptyView(message: 'No service visits on record.', icon: Icons.history),
          )
        else
          // Rendered in the order the API sent it — oldest first. A repeat failure only
          // reads as one in the order it happened, so nothing here re-sorts.
          for (var i = 0; i < history.length; i++)
            _ServiceRecordCard(record: history[i], index: i, total: history.length),
      ],
    );
  }
}

class _HeaderCard extends StatelessWidget {
  const _HeaderCard({required this.asset, required this.summary});

  final AssetDetail asset;
  final AsyncValue<FailureSummary> summary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
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
            const SizedBox(height: 12),
            Text(asset.name, style: theme.textTheme.headlineSmall),
            const SizedBox(height: 2),
            Text(
              [asset.categoryName, asset.makeAndModel].whereType<String>().join(' · '),
              style: theme.textTheme.bodyMedium?.copyWith(color: theme.colorScheme.outline),
            ),
            const Divider(height: 28),
            _Fact(label: 'Asset tag', value: asset.assetTag, monospace: true),
            _Fact(label: 'Room', value: '${asset.room.label} (floor ${asset.room.floor})'),
            _Fact(label: 'Installed', value: formatDateOnly(asset.installedOn)),
            _Fact(
              label: 'Warranty until',
              value: asset.warrantyExpiresOn == null
                  ? 'Not recorded'
                  : formatDateOnly(asset.warrantyExpiresOn),
            ),
          ],
        ),
      ),
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact({required this.label, required this.value, this.monospace = false});

  final String label;
  final String value;
  final bool monospace;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 112,
            child: Text(
              label,
              style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: theme.textTheme.bodyMedium?.copyWith(
                fontWeight: FontWeight.w500,
                fontFamily: monospace ? 'monospace' : null,
              ),
            ),
          ),
        ],
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
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);

    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: summary.when(
          loading: () => Row(
            children: [
              const SizedBox(
                width: 16,
                height: 16,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
              const SizedBox(width: 12),
              Text('Loading failure summary…', style: muted),
            ],
          ),
          error: (error, _) => Row(
            children: [
              Icon(Icons.error_outline, size: 18, color: theme.colorScheme.error),
              const SizedBox(width: 8),
              Expanded(child: Text('Failure summary unavailable. ${_messageFor(error)}')),
              TextButton(onPressed: onRetry, child: const Text('Retry')),
            ],
          ),
          data: (data) => Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (data.isRepeatFailure) ...[
                const RepeatFailureChip(),
                const SizedBox(height: 8),
                Text(
                  'Three or more service visits in the last 90 days. The pattern is in the '
                  'notes below, not on the asset record.',
                  style: theme.textTheme.bodySmall,
                ),
                const SizedBox(height: 12),
              ],
              Row(
                children: [
                  _Stat(label: 'Last 90 days', value: '${data.failureCount3Months}'),
                  _Stat(label: 'Last 12 months', value: '${data.failureCount12Months}'),
                  _Stat(label: 'Temporary fixes', value: '${data.temporaryFixCount}'),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                // Null is not zero: a machine nobody has touched was not serviced today.
                data.lastServicedOn == null
                    ? 'Never serviced.'
                    : 'Last serviced ${formatDateOnly(data.lastServicedOn)} — '
                        '${data.daysSinceLastService} '
                        '${data.daysSinceLastService == 1 ? 'day' : 'days'} ago.',
                style: muted,
              ),
            ],
          ),
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
          Text(value, style: theme.textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w700)),
          Text(
            label,
            style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline),
          ),
        ],
      ),
    );
  }
}

/// One visit. The technician's note is shown VERBATIM — no maxLines, no ellipsis, no
/// "read more" — because the fault this history is evidence of is spread across several
/// terse notes, and a note cut to its first line can drop exactly the clause that matters.
class _ServiceRecordCard extends StatelessWidget {
  const _ServiceRecordCard({required this.record, required this.index, required this.total});

  final ServiceRecord record;
  final int index;
  final int total;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final accent = outcomeColor(record.outcome);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    formatDateOnly(record.servicedOn),
                    style: theme.textTheme.titleSmall,
                  ),
                ),
                Text('Visit ${index + 1} of $total', style: muted),
              ],
            ),
            const SizedBox(height: 8),
            OutcomeChip(outcome: record.outcome),
            const SizedBox(height: 10),
            Container(
              width: double.infinity,
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
              decoration: BoxDecoration(
                color: theme.colorScheme.surfaceContainerHighest.withValues(alpha: 0.5),
                border: Border(left: BorderSide(color: accent, width: 3)),
              ),
              child: record.technicianNote == null || record.technicianNote!.isEmpty
                  ? Text('No note recorded.', style: muted?.copyWith(fontStyle: FontStyle.italic))
                  : SelectableText(
                      record.technicianNote!,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontFamily: 'monospace',
                        height: 1.45,
                      ),
                    ),
            ),
            const SizedBox(height: 8),
            Text(
              [
                record.technicianName,
                if (record.workOrderId != null) 'Work order #${record.workOrderId}',
              ].join(' · '),
              style: muted,
            ),
          ],
        ),
      ),
    );
  }
}
