import 'package:flutter/material.dart';

import '../../widgets/status_pill.dart';
import 'report.dart';

/// A report's status, keyed by the `ReportStatus` enum NAME, in the same tones as the web
/// client's `report-status--*` pills.
class ReportStatusChip extends StatelessWidget {
  const ReportStatusChip({super.key, required this.status});

  final String status;

  @override
  Widget build(BuildContext context) {
    final tone = switch (status) {
      ReportStatuses.awaitingClarification => PillTone.warn,
      ReportStatuses.workOrderRaised => PillTone.success,
      ReportStatuses.submitted ||
      ReportStatuses.clarified ||
      ReportStatuses.diagnosed =>
        PillTone.info,
      // Closed stays grey: it can mean fixed, a duplicate, or nothing at all.
      _ => PillTone.neutral,
    };
    return StatusPill(label: ReportStatuses.label(status), tone: tone);
  }
}
