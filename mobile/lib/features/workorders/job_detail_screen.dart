import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../../widgets/status_pill.dart';
import '../assets/asset_detail_screen.dart';
import '../reports/report.dart' show formatTimestamp;
import 'complete_job_screen.dart';
import 'work_order.dart';
import 'work_order_status_chip.dart';
import 'work_orders_api.dart';

/// One job, laid out so a technician arrives knowing what they are walking into: the
/// machine, the room, when they are booked, what to bring, what was reported and what the
/// diagnostic agent thinks is wrong.
///
/// Everything comes from ONE request, GET /api/workorders/{id} — a Technician cannot read
/// the report or the approval queue, so the order's detail carries the room and the
/// diagnosis for them. The reporter's words, the parts list and the agent's evidence are
/// rendered verbatim: no truncation, selectable, never tidied.
class JobDetailScreen extends ConsumerWidget {
  const JobDetailScreen({super.key, required this.workOrderId});

  /// Nested under the jobs list: `/jobs/7`.
  static const String subPath = ':id';

  static String location(int id) => '/jobs/$id';

  /// Null when the route carried something that is not an id — an error, not a crash.
  final int? workOrderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final id = workOrderId;
    if (id == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Job')),
        body: const ErrorView(title: 'Not a job', message: 'This link does not point at a job.'),
      );
    }

    final order = ref.watch(workOrderDetailProvider(id));

    return Scaffold(
      appBar: AppBar(title: Text(order.valueOrNull?.assetTag ?? 'Job #$id')),
      body: SafeArea(
        // All three states of the request are rendered explicitly.
        child: order.when(
          loading: () => const LoadingView(message: 'Loading job…'),
          error: (error, _) => ErrorView(
            // The API's 403 and 404 are different answers: someone else's job exists.
            title: switch (error) {
              ApiException(statusCode: 403) => 'Not your job',
              ApiException(statusCode: 404) => 'Job not found',
              _ => 'Could not load this job',
            },
            message: error is ApiException ? error.message : 'Could not reach the API.',
            onRetry: () => ref.invalidate(workOrderDetailProvider(id)),
          ),
          data: (detail) => RefreshIndicator(
            onRefresh: () => ref.refresh(workOrderDetailProvider(id).future),
            child: _JobDetail(
              detail: detail,
              isMine: detail.assignedTechnicianId != null &&
                  detail.assignedTechnicianId == ref.watch(currentUserIdProvider),
            ),
          ),
        ),
      ),
    );
  }
}

class _JobDetail extends StatelessWidget {
  const _JobDetail({required this.detail, required this.isMine});

  final WorkOrderDetail detail;
  final bool isMine;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
      children: [
        // The machine.
        Row(
          children: [
            WorkOrderStatusChip(status: detail.status),
            const SizedBox(width: 8),
            Flexible(
              child: StatusPill(
                label: WorkOrderStrategies.label(detail.strategy),
                tone: PillTone.neutral,
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        Text(detail.assetName, style: theme.textTheme.titleLarge),
        Text(
          [detail.assetTag, if (detail.makeAndModel != null) detail.makeAndModel!].join(' · '),
          style: muted,
        ),
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            onPressed: () => context.go(AssetDetailScreen.location(detail.assetId)),
            icon: const Icon(Icons.history),
            label: const Text('Service history'),
          ),
        ),

        _Section(
          icon: Icons.place_outlined,
          title: 'Where',
          child: Text(
            '${detail.room.code} · ${detail.room.name} · Floor ${detail.room.floor}',
            style: theme.textTheme.bodyLarge,
          ),
        ),

        _Section(
          icon: Icons.event_outlined,
          title: 'When',
          child: detail.scheduledSlots.isEmpty
              ? Text(
                  'No visit booked yet. The facilities manager books a time once the job is '
                  'assigned.',
                  style: muted,
                )
              : Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    for (final slot in detail.scheduledSlots)
                      Text(slot.label, style: theme.textTheme.bodyLarge),
                  ],
                ),
        ),

        _Section(
          icon: Icons.report_outlined,
          title: 'What was reported',
          child: SelectableText(detail.reportDescription, style: theme.textTheme.bodyLarge),
        ),

        _Section(
          icon: Icons.build_outlined,
          title: 'Parts required',
          child: detail.partsRequired == null || detail.partsRequired!.trim().isEmpty
              ? Text('None listed.', style: muted)
              : SelectableText(detail.partsRequired!, style: theme.textTheme.bodyLarge),
        ),

        _Section(
          icon: Icons.psychology_outlined,
          title: 'Diagnosis',
          child: _DiagnosisPanel(diagnosis: detail.diagnosis),
        ),

        if (detail.status == WorkOrderStatuses.completed) _CompletedPanel(detail: detail),

        if (detail.isCompletable && isMine) ...[
          const SizedBox(height: 24),
          FilledButton.icon(
            onPressed: () => context.go(CompleteJobScreen.location(detail.id)),
            icon: const Icon(Icons.task_alt),
            label: const Text('Complete job'),
          ),
        ],
      ],
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({required this.icon, required this.title, required this.child});

  final IconData icon;
  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(icon, size: 18, color: theme.colorScheme.primary),
              const SizedBox(width: 6),
              Text(title, style: theme.textTheme.titleSmall),
            ],
          ),
          const SizedBox(height: 6),
          child,
        ],
      ),
    );
  }
}

/// The agent's diagnosis, or which of the three reasons there is none. ADVICE — labelled
/// as such, and nothing on this screen acts on it.
class _DiagnosisPanel extends StatelessWidget {
  const _DiagnosisPanel({required this.diagnosis});

  final Diagnosis? diagnosis;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodyMedium?.copyWith(color: theme.colorScheme.outline);
    final d = diagnosis;

    // Null is not empty: never asked is a different fact from asked-and-failed.
    if (d == null) {
      return Text('The diagnostic agent has not looked at this fault.', style: muted);
    }
    if (d.failed) {
      return Text(
        'The diagnostic agent ran but could not produce a diagnosis.'
        '${d.errorMessage == null ? '' : '\nReason: ${d.errorMessage}'}',
        style: muted,
      );
    }
    if (!d.outputReadable) {
      return Text(
        'A diagnosis was recorded but could not be read. A facilities manager can see the '
        'raw record on the web.',
        style: muted,
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Advice from the diagnostic agent, not a decision.', style: muted),
        const SizedBox(height: 8),
        for (var i = 0; i < d.hypotheses.length; i++)
          _HypothesisCard(
            hypothesis: d.hypotheses[i],
            isPrimary: i == d.primaryHypothesisIndex,
          ),
        if (d.recommendedNextAction != null) ...[
          const SizedBox(height: 4),
          Text.rich(TextSpan(children: [
            const TextSpan(text: 'Suggested next step: '),
            TextSpan(
              text: d.recommendedNextAction,
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
          ])),
        ],
        if (d.reasoningSummary != null && d.reasoningSummary!.isNotEmpty) ...[
          const SizedBox(height: 6),
          SelectableText(d.reasoningSummary!, style: theme.textTheme.bodyMedium),
        ],
      ],
    );
  }
}

class _HypothesisCard extends StatelessWidget {
  const _HypothesisCard({required this.hypothesis, required this.isPrimary});

  final DiagnosisHypothesis hypothesis;
  final bool isPrimary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final tone = switch (hypothesis.confidence) {
      'high' => PillTone.info,
      'medium' => PillTone.warn,
      _ => PillTone.neutral,
    };

    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Wrap(
              spacing: 6,
              runSpacing: 4,
              children: [
                StatusPill(label: '${hypothesis.confidence} confidence', tone: tone),
                if (isPrimary) const StatusPill(label: 'Most likely', tone: PillTone.info),
              ],
            ),
            const SizedBox(height: 8),
            Text(hypothesis.cause, style: theme.textTheme.titleSmall),
            const SizedBox(height: 4),
            for (final item in hypothesis.evidence)
              Padding(
                padding: const EdgeInsets.only(top: 2),
                child: SelectableText('• $item', style: theme.textTheme.bodyMedium),
              ),
          ],
        ),
      ),
    );
  }
}

/// A finished job's record: what was done, what it cost, and the evidence photo.
class _CompletedPanel extends StatelessWidget {
  const _CompletedPanel({required this.detail});

  final WorkOrderDetail detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final url = detail.completionPhotoUrl;

    return _Section(
      icon: Icons.task_alt,
      title: 'What was done',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (detail.resolutionNote != null)
            SelectableText(detail.resolutionNote!, style: theme.textTheme.bodyLarge),
          const SizedBox(height: 6),
          Text(
            'Completed ${formatTimestamp(detail.completedAt)} · '
            'Cost ${formatMoney(detail.actualCost)}',
            style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline),
          ),
          if (url != null) ...[
            const SizedBox(height: 10),
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.network(
                url,
                height: 200,
                fit: BoxFit.cover,
                // A URL that does not load says so and shows the link, never a broken image.
                errorBuilder: (context, _, __) => Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('The photo could not be loaded.', style: theme.textTheme.bodyMedium),
                    SelectableText(url, style: theme.textTheme.bodySmall),
                  ],
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
