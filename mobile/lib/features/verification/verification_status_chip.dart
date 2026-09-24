import 'package:flutter/material.dart';

import '../../widgets/status_pill.dart';
import 'verification.dart';

/// A check's status, keyed by the `VerificationStatus` enum NAME, in the same tones as the
/// web client's `verification-status--*` pills.
class VerificationStatusChip extends StatelessWidget {
  const VerificationStatusChip({super.key, required this.status});

  final String status;

  @override
  Widget build(BuildContext context) {
    final tone = switch (status) {
      VerificationStatuses.pending ||
      VerificationStatuses.awaitingReporterResponse =>
        PillTone.info,
      VerificationStatuses.confirmed => PillTone.success,
      VerificationStatuses.reopened => PillTone.warn,
      VerificationStatuses.escalated => PillTone.danger,
      // Expired stays grey: silence is not a verdict either way.
      _ => PillTone.neutral,
    };
    return StatusPill(label: VerificationStatuses.label(status), tone: tone);
  }
}

/// The agent's label as a pill, in the web client's `agent-outcome--*` tones. Unknown values
/// render raw and grey, like the web client.
class AgentOutcomeChip extends StatelessWidget {
  const AgentOutcomeChip({super.key, required this.outcome});

  final String outcome;

  @override
  Widget build(BuildContext context) {
    final (label, tone) = switch (outcome) {
      AgentOutcomes.confirm => ('Review: holding', PillTone.success),
      AgentOutcomes.reopen => ('Review: reopen', PillTone.warn),
      AgentOutcomes.escalate => ('Review: escalate', PillTone.danger),
      _ => (outcome, PillTone.neutral),
    };
    return StatusPill(label: label, tone: tone, icon: Icons.fact_check_outlined);
  }
}

/// What happened to the repair, as a line on the reporter's own report card.
///
/// The report's status stops at WorkOrderRaised or Closed and cannot say whether the repair
/// held — so without this, a reporter who answered "still broken" would see nothing on their
/// report change. The status is the reporter's answer (C#); the agent's flag is shown beside
/// it when it thinks the repair did not hold, as a review, never as a verdict.
class ReportVerificationLine extends StatelessWidget {
  const ReportVerificationLine({super.key, required this.verification});

  final ReportVerification verification;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final waiting = verification.isWaitingOnReporter;
    final outcome = verification.agentOutcome;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text('Repair', style: theme.textTheme.labelMedium),
            const SizedBox(width: 8),
            VerificationStatusChip(status: verification.status),
            if (outcome != null && AgentOutcomes.flagsFollowUp(outcome)) ...[
              const SizedBox(width: 6),
              Flexible(child: AgentOutcomeChip(outcome: outcome)),
            ],
          ],
        ),
        const SizedBox(height: 6),
        Row(
          children: [
            Expanded(
              child: Text(
                describeForReporter(verification.status),
                style: waiting
                    ? theme.textTheme.bodyMedium?.copyWith(
                        color: theme.colorScheme.primary,
                        fontWeight: FontWeight.w600,
                      )
                    : theme.textTheme.bodySmall,
              ),
            ),
            if (waiting) ...[
              Text('Answer', style: TextStyle(color: theme.colorScheme.primary)),
              Icon(Icons.chevron_right, color: theme.colorScheme.primary),
            ],
          ],
        ),
      ],
    );
  }
}
