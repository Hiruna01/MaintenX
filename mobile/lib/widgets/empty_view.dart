import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../core/app_theme.dart';

/// The "the request worked, there is just nothing to show" state. Distinct from an error
/// on purpose: an empty list is not a failure and must not look like one — grey, never red.
class EmptyView extends StatelessWidget {
  const EmptyView({
    super.key,
    required this.message,
    this.icon = LucideIcons.inbox,
    this.action,
  });

  final String message;
  final IconData icon;

  /// Something to do about it — "Clear filters", "Submit a report". Never a retry: there
  /// was nothing to retry.
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 64,
              height: 64,
              decoration: BoxDecoration(
                color: MxColors.surface,
                borderRadius: BorderRadius.circular(22),
                border: Border.all(color: MxColors.hairline),
              ),
              child: Icon(icon, size: 26, color: MxColors.graphite),
            ),
            const SizedBox(height: 18),
            ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 300),
              child: Text(
                message,
                textAlign: TextAlign.center,
                style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
              ),
            ),
            if (action != null) ...[
              const SizedBox(height: 20),
              action!,
            ],
          ],
        ),
      ),
    );
  }
}
