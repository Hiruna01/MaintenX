import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../widgets/status_pill.dart';
import '../assets/asset.dart' show splitPascalCase;

/// Mirrors the API's `ReportStage` enum — where a report has got to, in its REPORTER's words.
/// Matched by NAME, never by ordinal.
///
/// The stage is DERIVED IN C# (`ReportProgress.StageFor`) from the report's status, its latest
/// workflow and whether its order was rejected. Nothing here decides a stage; this only words
/// and colours the one the API sent. It never carries a cost, an estimate or a technician.
class ReportStages {
  const ReportStages._();

  static const String beingReviewed = 'BeingReviewed';
  static const String waitingOnYou = 'WaitingOnYou';
  static const String awaitingApproval = 'AwaitingApproval';
  static const String repairPlanned = 'RepairPlanned';
  static const String repaired = 'Repaired';
  static const String notGoingAhead = 'NotGoingAhead';
  static const String closed = 'Closed';

  static String label(String stage) => splitPascalCase(stage);

  /// One sentence for the reporter. An unknown stage — one added to the C# enum first — is
  /// shown by its name rather than guessed at.
  static String describe(String stage) => switch (stage) {
        beingReviewed => 'Facilities are looking into what is wrong.',
        waitingOnYou => 'A few questions are waiting on you before the work can go ahead.',
        awaitingApproval => 'A repair has been proposed and is waiting for a manager to sign it off.',
        repairPlanned => 'The repair has been approved and is being arranged.',
        repaired => 'The work is done. We will check with you that it held.',
        notGoingAhead => 'A manager decided not to go ahead with this repair.',
        closed => 'This report is closed.',
        _ => 'Stage: ${label(stage)}',
      };

  static (PillTone, IconData) _look(String stage) => switch (stage) {
        beingReviewed => (PillTone.info, LucideIcons.search),
        waitingOnYou => (PillTone.warn, LucideIcons.messageCircleQuestion),
        awaitingApproval => (PillTone.info, LucideIcons.scale),
        repairPlanned => (PillTone.info, LucideIcons.wrench),
        repaired => (PillTone.success, LucideIcons.circleCheck),
        // Grey, not red: a manager saying no is the control working, not the system failing.
        notGoingAhead => (PillTone.neutral, LucideIcons.ban),
        _ => (PillTone.neutral, LucideIcons.archive),
      };
}

/// The report's stage as a line on the reporter's card: a pill and one sentence. Display
/// only — the stage is the API's.
class ReportStageLine extends StatelessWidget {
  const ReportStageLine({super.key, required this.stage});

  final String stage;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final (tone, icon) = ReportStages._look(stage);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text('Progress', style: theme.textTheme.labelMedium),
            const SizedBox(width: 8),
            Flexible(
              child: StatusPill(label: ReportStages.label(stage), tone: tone, icon: icon),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Text(ReportStages.describe(stage), style: theme.textTheme.bodySmall),
      ],
    );
  }
}
