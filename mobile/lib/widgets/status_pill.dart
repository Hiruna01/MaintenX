import 'package:flutter/material.dart';

/// The same five tones the web client's pills use (`--success`, `--warn`, `--info`,
/// `--neutral`, `--danger` in `index.css`), so a status reads the same colour on both
/// clients. A new pill picks one of these rather than introducing a colour of its own.
class PillTone {
  const PillTone._(this.foreground, this.background, this.border);

  final Color foreground;
  final Color background;
  final Color border;

  static const success = PillTone._(Color(0xFF067647), Color(0xFFECFDF3), Color(0xFFABEFC6));
  static const warn = PillTone._(Color(0xFFB54708), Color(0xFFFFFAEB), Color(0xFFFEDF89));
  static const info = PillTone._(Color(0xFF175CD3), Color(0xFFEFF8FF), Color(0xFFB2DDFF));
  static const neutral = PillTone._(Color(0xFF475467), Color(0xFFF2F4F7), Color(0xFFE4E7EC));
  static const danger = PillTone._(Color(0xFFB42318), Color(0xFFFDF1F0), Color(0xFFFECDCA));
}

/// A small rounded label in one [PillTone].
class StatusPill extends StatelessWidget {
  const StatusPill({super.key, required this.label, required this.tone, this.icon});

  final String label;
  final PillTone tone;
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
