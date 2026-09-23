import 'package:flutter/material.dart';

import 'asset.dart';

/// The same four tones the web client's pills use, so an asset reads the same colour on
/// both clients.
class _Tone {
  const _Tone(this.foreground, this.background, this.border);

  final Color foreground;
  final Color background;
  final Color border;

  static const success = _Tone(Color(0xFF067647), Color(0xFFECFDF3), Color(0xFFABEFC6));
  static const warn = _Tone(Color(0xFFB54708), Color(0xFFFFFAEB), Color(0xFFFEDF89));
  static const info = _Tone(Color(0xFF175CD3), Color(0xFFEFF8FF), Color(0xFFB2DDFF));
  static const neutral = _Tone(Color(0xFF475467), Color(0xFFF2F4F7), Color(0xFFE4E7EC));
  static const danger = _Tone(Color(0xFFB42318), Color(0xFFFDF1F0), Color(0xFFFECDCA));
}

class _Pill extends StatelessWidget {
  const _Pill({required this.label, required this.tone, this.icon});

  final String label;
  final _Tone tone;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: tone.background,
        border: Border.all(color: tone.border),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 14, color: tone.foreground),
            const SizedBox(width: 4),
          ],
          Flexible(
            child: Text(
              label,
              style: TextStyle(
                color: tone.foreground,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// An asset's status, keyed by the `AssetStatus` enum NAME.
class AssetStatusChip extends StatelessWidget {
  const AssetStatusChip({super.key, required this.status});

  final String status;

  @override
  Widget build(BuildContext context) {
    final tone = switch (status) {
      AssetStatuses.active => _Tone.success,
      AssetStatuses.underMaintenance => _Tone.warn,
      _ => _Tone.neutral,
    };
    return _Pill(label: AssetStatuses.label(status), tone: tone);
  }
}

/// A service visit's outcome, keyed by the `ServiceOutcome` enum NAME.
class OutcomeChip extends StatelessWidget {
  const OutcomeChip({super.key, required this.outcome});

  final String outcome;

  @override
  Widget build(BuildContext context) {
    return _Pill(label: ServiceOutcomes.label(outcome), tone: _outcomeTone(outcome));
  }
}

/// The accent colour of a visit's outcome — also used for the rule beside its note.
Color outcomeColor(String outcome) => _outcomeTone(outcome).foreground;

_Tone _outcomeTone(String outcome) => switch (outcome) {
      ServiceOutcomes.resolved => _Tone.success,
      ServiceOutcomes.temporaryFix => _Tone.warn,
      ServiceOutcomes.partReplaced => _Tone.info,
      _ => _Tone.neutral,
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
      return const _Pill(label: 'Checking warranty…', tone: _Tone.neutral);
    }
    if (isUnderWarranty == null) {
      return const _Pill(label: 'Warranty status unavailable', tone: _Tone.neutral);
    }
    if (isUnderWarranty!) {
      return _Pill(
        label: 'Under warranty · until ${formatDateOnly(warrantyExpiresOn)}',
        tone: _Tone.success,
        icon: Icons.verified_user_outlined,
      );
    }
    // A null expiry is "no warranty recorded" — the same grey as expired, but a different
    // fact, so it says so.
    return _Pill(
      label: warrantyExpiresOn == null
          ? 'No warranty recorded'
          : 'Warranty expired · ${formatDateOnly(warrantyExpiresOn)}',
      tone: _Tone.neutral,
      icon: Icons.shield_outlined,
    );
  }
}

/// The red "Repeat failure" marker, shown only when the API's summary says so.
class RepeatFailureChip extends StatelessWidget {
  const RepeatFailureChip({super.key});

  @override
  Widget build(BuildContext context) {
    return const _Pill(
      label: 'Repeat failure',
      tone: _Tone.danger,
      icon: Icons.warning_amber_rounded,
    );
  }
}
