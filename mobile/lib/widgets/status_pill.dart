import 'package:flutter/material.dart';

/// The same five tones the web client's pills use (`--mx-green`, `--mx-amber`, `--mx-blue`,
/// `--mx-slate`, `--mx-red` in `components/ui/tokens.css`), so a status reads the same colour
/// on both clients. A new pill picks one of these rather than introducing a colour of its own.
class PillTone {
  const PillTone._(this.foreground, this.background, this.border);

  final Color foreground;
  final Color background;
  final Color border;

  static const success = PillTone._(Color(0xFF0E8A4F), Color(0xFFEAF7F0), Color(0xFFCDEBD9));
  static const warn = PillTone._(Color(0xFFB25E09), Color(0xFFFDF4E6), Color(0xFFF4DCB4));
  static const info = PillTone._(Color(0xFF2563C9), Color(0xFFEBF2FD), Color(0xFFCDDCF7));
  static const neutral = PillTone._(Color(0xFF5C6370), Color(0xFFF1F2F4), Color(0xFFE1E3E7));
  static const danger = PillTone._(Color(0xFFC2341D), Color(0xFFFDEEEB), Color(0xFFF6D0C9));
}

/// A small rounded label in one [PillTone], led by a dot in its colour (or [icon]).
class StatusPill extends StatelessWidget {
  const StatusPill({super.key, required this.label, required this.tone, this.icon});

  final String label;
  final PillTone tone;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(8, 4, 10, 4),
      decoration: BoxDecoration(
        color: tone.background,
        border: Border.all(color: tone.border),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null)
            Icon(icon, size: 13, color: tone.foreground)
          else
            Container(
              width: 6,
              height: 6,
              decoration: BoxDecoration(color: tone.foreground, shape: BoxShape.circle),
            ),
          const SizedBox(width: 6),
          Flexible(
            child: Text(
              label,
              style: TextStyle(
                color: tone.foreground,
                fontSize: 12,
                height: 1.2,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
