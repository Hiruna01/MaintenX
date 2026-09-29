import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../../widgets/status_pill.dart';
import '../../widgets/surfaces.dart';
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
        body: const ErrorView(title: 'Not a job', message: 'This link does not point at a job.'),
      );
    }

    final order = ref.watch(workOrderDetailProvider(id));

    return Scaffold(
      appBar: appBar,
      body: SafeArea(
        top: false,
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
          data: (detail) {
            final canComplete = detail.isCompletable &&
                detail.assignedTechnicianId != null &&
                detail.assignedTechnicianId == ref.watch(currentUserIdProvider);

            return Column(
              children: [
                Expanded(
                  child: RefreshIndicator(
                    color: MxColors.ink,
                    backgroundColor: MxColors.surface,
                    onRefresh: () => ref.refresh(workOrderDetailProvider(id).future),
                    child: _JobDetail(detail: detail),
                  ),
                ),
                // Offered only on live work assigned to the signed-in user; the API is the
                // rule either way.
                if (canComplete)
                  MxActionBar(
                    color: MxColors.canvas,
                    children: [
                      FilledButton.icon(
                        onPressed: () => context.go(CompleteJobScreen.location(detail.id)),
                        icon: const Icon(LucideIcons.circleCheck, size: 18),
                        label: const Text('Complete job'),
                      ),
                    ],
                  ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _JobDetail extends StatelessWidget {
  const _JobDetail({required this.detail});

  final WorkOrderDetail detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite);
    final body = theme.textTheme.bodyLarge?.copyWith(height: 1.45);

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
      children: [
        // The machine.
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            WorkOrderStatusChip(status: detail.status),
            StatusPill(label: WorkOrderStrategies.label(detail.strategy), tone: PillTone.neutral),
          ],
        ),
        const SizedBox(height: 14),
        Text(detail.assetName, style: theme.textTheme.headlineMedium),
        const SizedBox(height: 4),
        Text(
          [detail.assetTag, if (detail.makeAndModel != null) detail.makeAndModel!].join(', '),
          style: muted?.copyWith(fontFeatures: MxType.tabular),
        ),
        const SizedBox(height: 14),
        Align(
          alignment: Alignment.centerLeft,
          child: OutlinedButton.icon(
            onPressed: () => context.go(AssetDetailScreen.location(detail.assetId)),
            style: OutlinedButton.styleFrom(
              backgroundColor: MxColors.surface,
              minimumSize: const Size(0, 42),
            ),
            icon: const Icon(LucideIcons.history, size: 16),
            label: const Text('Service history'),
          ),
        ),
        const SizedBox(height: 20),

        // Where and when, together: the two facts that get someone to the job.
        MxCard(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Column(
            children: [
              _FactRow(
                icon: LucideIcons.mapPin,
                label: 'Where',
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(detail.room.name, style: theme.textTheme.titleMedium),
                    Text(
                      '${detail.room.code}, floor ${detail.room.floor}',
                      style: theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular),
                    ),
                  ],
                ),
              ),
              const Divider(indent: 72, endIndent: 18),
              _FactRow(
                icon: LucideIcons.calendarClock,
                label: 'When',
                child: detail.scheduledSlots.isEmpty
                    ? Text(
                        'No visit booked yet. The facilities manager books a time once the job '
                        'is assigned.',
                        style: muted,
                      )
                    : Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          for (final slot in detail.scheduledSlots)
                            Padding(
                              padding: const EdgeInsets.only(bottom: 2),
                              child: Text(
                                slot.label,
                                style: theme.textTheme.titleMedium?.copyWith(
                                  fontFeatures: MxType.tabular,
                                ),
                              ),
                            ),
                        ],
                      ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),

        _Section(
          icon: LucideIcons.messageSquareText,
          title: 'What was reported',
          child: SelectableText(detail.reportDescription, style: body),
        ),
        const SizedBox(height: 12),

        _Section(
          icon: LucideIcons.package,
          title: 'Parts to bring',
          child: detail.partsRequired == null || detail.partsRequired!.trim().isEmpty
              ? Text('None listed.', style: muted)
              : SelectableText(detail.partsRequired!, style: body),
        ),
        const SizedBox(height: 12),

        _Section(
          icon: LucideIcons.sparkles,
          title: 'Diagnosis',
          trailing: const StatusPill(label: 'Advice', tone: PillTone.neutral),
          child: _DiagnosisPanel(diagnosis: detail.diagnosis),
        ),

        if (detail.status == WorkOrderStatuses.completed) ...[
          const SizedBox(height: 12),
          _CompletedPanel(detail: detail),
        ],
      ],
    );
  }
}

/// An icon tile, a small label and the fact itself — one row of the where/when card.
class _FactRow extends StatelessWidget {
  const _FactRow({required this.icon, required this.label, required this.child});

  final IconData icon;
  final String label;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(18, 14, 18, 14),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          MxIconTile(icon: icon, size: 40),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                MxPanelLabel(label),
                const SizedBox(height: 4),
                child,
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// A white card with a labelled heading.
class _Section extends StatelessWidget {
  const _Section({
    required this.icon,
    required this.title,
    required this.child,
    this.trailing,
  });

  final IconData icon;
  final String title;
  final Widget child;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return MxCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: MxPanelLabel(title, icon: icon)),
              if (trailing != null) trailing!,
            ],
          ),
          const SizedBox(height: 10),
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
    final muted = theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite);
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
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'Advice from the diagnostic agent, not a decision.',
          style: theme.textTheme.bodySmall,
        ),
        const SizedBox(height: 12),
        for (var i = 0; i < d.hypotheses.length; i++)
          _HypothesisCard(
            hypothesis: d.hypotheses[i],
            isPrimary: i == d.primaryHypothesisIndex,
          ),
        if (d.recommendedNextAction != null) ...[
          const SizedBox(height: 4),
          Row(
            children: [
              const Icon(LucideIcons.cornerDownRight, size: 16, color: MxColors.graphite),
              const SizedBox(width: 8),
              Expanded(
                child: Text.rich(TextSpan(children: [
                  const TextSpan(text: 'Suggested next step: '),
                  TextSpan(
                    text: d.recommendedNextAction,
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                ])),
              ),
            ],
          ),
        ],
        if (d.reasoningSummary != null && d.reasoningSummary!.isNotEmpty) ...[
          const SizedBox(height: 10),
          SelectableText(d.reasoningSummary!, style: muted),
        ],
      ],
    );
  }
}

/// One possible cause and the evidence behind it, verbatim. The most likely one gets an ink
/// edge; the rest sit flat.
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

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: MxColors.well,
        borderRadius: BorderRadius.circular(MxRadii.md),
        border: Border.all(
          color: isPrimary ? MxColors.ink : Colors.transparent,
          width: 1.4,
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 6,
            runSpacing: 4,
            children: [
              StatusPill(label: '${hypothesis.confidence} confidence', tone: tone),
              if (isPrimary) const StatusPill(label: 'Most likely', tone: PillTone.neutral),
            ],
          ),
          const SizedBox(height: 10),
          Text(hypothesis.cause, style: theme.textTheme.titleSmall?.copyWith(fontSize: 15)),
          const SizedBox(height: 6),
          for (final item in hypothesis.evidence)
            Padding(
              padding: const EdgeInsets.only(top: 3),
              child: SelectableText(
                '• $item',
                style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.ink2),
              ),
            ),
        ],
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
    final meta = theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular);

    return _Section(
      icon: LucideIcons.circleCheck,
      title: 'What was done',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (detail.resolutionNote != null)
            SelectableText(
              detail.resolutionNote!,
              style: theme.textTheme.bodyLarge?.copyWith(height: 1.45),
            ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: Text('Completed ${formatTimestamp(detail.completedAt)}', style: meta),
              ),
              Text(
                formatMoney(detail.actualCost),
                style: theme.textTheme.titleSmall?.copyWith(fontFeatures: MxType.tabular),
              ),
            ],
          ),
          if (url != null) ...[
            const SizedBox(height: 12),
            ClipRRect(
              borderRadius: BorderRadius.circular(MxRadii.md),
              child: Image.network(
                url,
                height: 200,
                width: double.infinity,
                fit: BoxFit.cover,
                // A URL that does not load says so and shows the link, never a broken image.
                errorBuilder: (context, _, __) => MxWell(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('The photo could not be loaded.', style: theme.textTheme.bodyMedium),
                      SelectableText(url, style: theme.textTheme.bodySmall),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
