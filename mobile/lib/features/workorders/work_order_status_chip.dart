import 'package:flutter/material.dart';

import '../../widgets/status_pill.dart';
import 'work_order.dart';

/// A work order's status, keyed by the `WorkOrderStatus` enum NAME, in the same tones as
/// the web client's `work-order-status--*` pills.
class WorkOrderStatusChip extends StatelessWidget {
  const WorkOrderStatusChip({super.key, required this.status});

  final String status;

  @override
  Widget build(BuildContext context) {
    final tone = switch (status) {
      WorkOrderStatuses.awaitingApproval => PillTone.warn,
      WorkOrderStatuses.approved ||
      WorkOrderStatuses.scheduled ||
      WorkOrderStatuses.inProgress =>
        PillTone.info,
      WorkOrderStatuses.completed => PillTone.success,
      WorkOrderStatuses.rejected => PillTone.danger,
      // Draft and Cancelled stay grey.
      _ => PillTone.neutral,
    };
    return StatusPill(label: WorkOrderStatuses.label(status), tone: tone);
  }
}
