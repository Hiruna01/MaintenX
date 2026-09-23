import 'package:flutter/material.dart';

import '../../widgets/status_pill.dart';
import 'asset.dart';

/// An asset's status, keyed by the `AssetStatus` enum NAME.
class AssetStatusChip extends StatelessWidget {
  const AssetStatusChip({super.key, required this.status});

  final String status;

  @override
  Widget build(BuildContext context) {
    final tone = switch (status) {
      AssetStatuses.active => PillTone.success,
      AssetStatuses.underMaintenance => PillTone.warn,
      _ => PillTone.neutral,
    };
    return StatusPill(label: AssetStatuses.label(status), tone: tone);
  }
}

/// A service visit's outcome, keyed by the `ServiceOutcome` enum NAME.
class OutcomeChip extends StatelessWidget {
  const OutcomeChip({super.key, required this.outcome});

  final String outcome;

  @override
  Widget build(BuildContext context) {
    return StatusPill(label: ServiceOutcomes.label(outcome), tone: _outcomeTone(outcome));
  }
}

/// The accent colour of a visit's outcome — also used for the rule beside its note.
Color outcomeColor(String outcome) => _outcomeTone(outcome).foreground;

PillTone _outcomeTone(String outcome) => switch (outcome) {
      ServiceOutcomes.resolved => PillTone.success,
      ServiceOutcomes.temporaryFix => PillTone.warn,
      ServiceOutcomes.partReplaced => PillTone.info,
      _ => PillTone.neutral,
    };

/// Green when under warranty, grey when not.
///
/// [isUnderWarranty] is the API's answer from the failure summary. This chip does NOT
/// compare the expiry date with today: warranty dates are a deterministic business rule,
/// so the rule lives in C# and this only picks a colour for the answer. The date is shown
/// beside it so a reader can see what the answer was based on.
///
/// Null [isUnderWarranty] means the summary has not arrived (or failed) — said as such,
/// never guessed.
class WarrantyChip extends StatelessWidget {
  const WarrantyChip({
    super.key,
    required this.isUnderWarranty,
    required this.warrantyExpiresOn,
    this.isLoading = false,
  });

  final bool? isUnderWarranty;
  final String? warrantyExpiresOn;
  final bool isLoading;

  @override
  Widget build(BuildContext context) {
    if (isLoading) {
      return const StatusPill(label: 'Checking warranty…', tone: PillTone.neutral);
    }
    if (isUnderWarranty == null) {
      return const StatusPill(label: 'Warranty status unavailable', tone: PillTone.neutral);
    }
    if (isUnderWarranty!) {
      return StatusPill(
        label: 'Under warranty · until ${formatDateOnly(warrantyExpiresOn)}',
        tone: PillTone.success,
        icon: Icons.verified_user_outlined,
      );
    }
    // A null expiry is "no warranty recorded" — the same grey as expired, but a different
    // fact, so it says so.
    return StatusPill(
      label: warrantyExpiresOn == null
          ? 'No warranty recorded'
          : 'Warranty expired · ${formatDateOnly(warrantyExpiresOn)}',
      tone: PillTone.neutral,
      icon: Icons.shield_outlined,
    );
  }
}

/// The red "Repeat failure" marker, shown only when the API's summary says so.
class RepeatFailureChip extends StatelessWidget {
  const RepeatFailureChip({super.key});

  @override
  Widget build(BuildContext context) {
    return const StatusPill(
      label: 'Repeat failure',
      tone: PillTone.danger,
      icon: Icons.warning_amber_rounded,
    );
  }
}
