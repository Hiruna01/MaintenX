import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/app_theme.dart';
import '../../widgets/surfaces.dart';
import '../assets/scan_asset_screen.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_state.dart';
import '../reports/my_reports_screen.dart';
import '../reports/submit_report_screen.dart';
import '../verification/pending_confirmations_screen.dart';
import '../workorders/my_jobs_screen.dart';
import '../workorders/work_order.dart';
import 'home_counts.dart';

/// The landing screen: one dark card with what matters to this role right now, then the
/// rest of what the app can do.
///
/// Navigation is role-based — "My jobs" is offered to a Technician only, since the API gives
/// nobody else a queue of their own there, and "Confirm repairs" to a Reporter only: the API
/// takes a repair's confirmation from the reporter who filed the fault and nobody else, and
/// a manager's list there would be every reporter's checks, none of them theirs to answer.
///
/// Every number on the card is the API's `totalCount` (see home_counts.dart); this screen
/// only shows it.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  static const String path = '/';

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  GoRouter? _router;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // Home stays mounted under every screen it opens, so its counts would otherwise still
    // say "1 to answer" after the answer was sent. Coming back to "/" reads them again.
    final router = GoRouter.maybeOf(context);
    if (router != _router) {
      _router?.routerDelegate.removeListener(_onRouteChanged);
      _router = router?..routerDelegate.addListener(_onRouteChanged);
    }
  }

  @override
  void dispose() {
    _router?.routerDelegate.removeListener(_onRouteChanged);
    super.dispose();
  }

  void _onRouteChanged() {
    final location = _router?.routerDelegate.currentConfiguration.uri.path;
    if (location == HomeScreen.path && mounted) _refreshCounts();
  }

  void _refreshCounts() {
    ref.invalidate(reportsAwaitingAnswerCountProvider);
    ref.invalidate(repairsToConfirmCountProvider);
    ref.invalidate(jobsInStatusCountProvider);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final user = ref.watch(authControllerProvider.select((state) => state.user));
    final role = user?.role;

    return Scaffold(
      body: SafeArea(
        child: RefreshIndicator(
          color: MxColors.ink,
          backgroundColor: MxColors.surface,
          onRefresh: () async => _refreshCounts(),
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 32),
            children: [
              Row(
                children: [
                  const MxWordmark(),
                  const Spacer(),
                  MxRoundButton(
                    tooltip: 'Sign out',
                    icon: LucideIcons.logOut,
                    onPressed: () => ref.read(authControllerProvider.notifier).logout(),
                  ),
                ],
              ),
              const SizedBox(height: 28),
              Text(
                _greeting(),
                style: theme.textTheme.headlineMedium?.copyWith(color: MxColors.mute),
              ),
              if (user != null) ...[
                Text(user.fullName, style: theme.textTheme.displaySmall),
                const SizedBox(height: 10),
                _RoleTag(label: Roles.label(role)),
              ],
              const SizedBox(height: 24),
              switch (role) {
                Roles.reporter => const _ReporterCard(),
                Roles.technician => const _TechnicianCard(),
                _ => const _AnyoneCard(),
              },
              const SizedBox(height: 28),
              _Shortcuts(role: role),
            ],
          ),
        ),
      ),
    );
  }

  /// Display only: the phone's own clock, for the greeting.
  static String _greeting() {
    final hour = DateTime.now().hour;
    if (hour < 12) return 'Good morning,';
    if (hour < 17) return 'Good afternoon,';
    return 'Good evening,';
  }
}

class _RoleTag extends StatelessWidget {
  const _RoleTag({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        decoration: BoxDecoration(
          color: MxColors.surface,
          borderRadius: BorderRadius.circular(999),
          border: Border.all(color: MxColors.hairline),
        ),
        child: Text(label, style: Theme.of(context).textTheme.labelMedium),
      ),
    );
  }
}

// ── The dark card ────────────────────────────────────────────────────────────────────────

/// The ink card at the top of Home. Its contents depend on the role; its two actions —
/// report a fault, scan a sticker — are open to everyone, as they are in the API.
class _InkCard extends StatelessWidget {
  const _InkCard({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.fromLTRB(20, 20, 20, 16),
      decoration: BoxDecoration(
        color: MxColors.ink,
        borderRadius: BorderRadius.circular(MxRadii.xl),
        boxShadow: mxFloatShadow,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            title,
            style: theme.textTheme.titleMedium?.copyWith(color: Colors.white),
          ),
          const SizedBox(height: 14),
          ...children,
          const SizedBox(height: 16),
          const _InkActions(),
        ],
      ),
    );
  }
}

class _InkActions extends StatelessWidget {
  const _InkActions();

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: FilledButton.icon(
            onPressed: () => context.go(SubmitReportScreen.path),
            style: FilledButton.styleFrom(
              backgroundColor: Colors.white,
              foregroundColor: MxColors.ink,
              minimumSize: const Size(0, 50),
              padding: const EdgeInsets.symmetric(horizontal: 12),
            ),
            icon: const Icon(LucideIcons.plus, size: 18),
            label: const Text('Report a fault'),
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: FilledButton.icon(
            onPressed: () => context.go(ScanAssetScreen.path),
            style: FilledButton.styleFrom(
              backgroundColor: Colors.white.withValues(alpha: 0.12),
              foregroundColor: Colors.white,
              minimumSize: const Size(0, 50),
              padding: const EdgeInsets.symmetric(horizontal: 12),
            ),
            icon: const Icon(LucideIcons.scanQrCode, size: 18),
            label: const Text('Scan a sticker'),
          ),
        ),
      ],
    );
  }
}

/// One count on the dark card: the API's number, what it counts, and where it leads.
/// Loading is a skeleton, a failure is a dash — the row still goes to its list, which has
/// its own error state and a retry.
class _CountRow extends StatelessWidget {
  const _CountRow({
    required this.count,
    required this.singular,
    required this.plural,
    required this.onTap,
    this.isOwed = false,
  });

  final AsyncValue<int> count;
  final String singular;
  final String plural;
  final VoidCallback onTap;

  /// Something here is waiting on the signed-in user — the one place iris appears.
  final bool isOwed;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final value = count.valueOrNull;
    final highlighted = isOwed && (value ?? 0) > 0;

    return Material(
      color: highlighted ? MxColors.iris : Colors.white.withValues(alpha: 0.07),
      borderRadius: BorderRadius.circular(MxRadii.md),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(MxRadii.md),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 12, 12),
          child: Row(
            children: [
              ConstrainedBox(
                constraints: const BoxConstraints(minWidth: 30),
                child: count.when(
                  loading: () => const Align(
                    alignment: Alignment.centerLeft,
                    widthFactor: 1,
                    child: _InkSkeleton(width: 30, height: 30),
                  ),
                  error: (_, __) => Text(
                    '—',
                    style: theme.textTheme.headlineMedium?.copyWith(color: Colors.white54),
                  ),
                  data: (n) => Text(
                    '$n',
                    style: theme.textTheme.headlineMedium?.copyWith(
                      color: Colors.white,
                      fontFeatures: MxType.tabular,
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  count.hasError
                      ? 'Could not load. Tap to open the list.'
                      : (value == 1 ? singular : plural),
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: highlighted ? Colors.white : Colors.white70,
                    fontWeight: highlighted ? FontWeight.w500 : FontWeight.w400,
                  ),
                ),
              ),
              Icon(
                LucideIcons.chevronRight,
                size: 18,
                color: highlighted ? Colors.white : Colors.white54,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _InkSkeleton extends StatelessWidget {
  const _InkSkeleton({required this.width, required this.height});

  final double width;
  final double height;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(8),
      ),
    );
  }
}

class _ReporterCard extends ConsumerWidget {
  const _ReporterCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return _InkCard(
      title: 'Waiting on you',
      children: [
        _CountRow(
          count: ref.watch(reportsAwaitingAnswerCountProvider),
          singular: 'report has questions for you',
          plural: 'reports have questions for you',
          isOwed: true,
          onTap: () => context.go(MyReportsScreen.path),
        ),
        const SizedBox(height: 8),
        _CountRow(
          count: ref.watch(repairsToConfirmCountProvider),
          singular: 'repair to confirm',
          plural: 'repairs to confirm',
          isOwed: true,
          onTap: () => context.go(PendingConfirmationsScreen.path),
        ),
      ],
    );
  }
}

class _TechnicianCard extends ConsumerWidget {
  const _TechnicianCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return _InkCard(
      title: 'Your jobs',
      children: [
        _CountRow(
          count: ref.watch(jobsInStatusCountProvider(WorkOrderStatuses.approved)),
          singular: 'approved, not yet scheduled',
          plural: 'approved, not yet scheduled',
          onTap: () => context.go(MyJobsScreen.path),
        ),
        const SizedBox(height: 8),
        _CountRow(
          count: ref.watch(jobsInStatusCountProvider(WorkOrderStatuses.scheduled)),
          singular: 'scheduled visit',
          plural: 'scheduled visits',
          onTap: () => context.go(MyJobsScreen.path),
        ),
      ],
    );
  }
}

/// A manager or an Admin: their own work is on the web client, so the card is the two
/// things anyone can do from a phone in a room.
class _AnyoneCard extends StatelessWidget {
  const _AnyoneCard();

  @override
  Widget build(BuildContext context) {
    return _InkCard(
      title: 'Something broken?',
      children: [
        Text(
          "Report it from where you are, or scan a machine's sticker to see its service "
          'history.',
          style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: Colors.white70),
        ),
      ],
    );
  }
}

// ── Everything else ──────────────────────────────────────────────────────────────────────

class _Shortcuts extends StatelessWidget {
  const _Shortcuts({required this.role});

  final String? role;

  @override
  Widget build(BuildContext context) {
    final rows = <Widget>[
      if (role == Roles.technician)
        _ShortcutRow(
          icon: LucideIcons.wrench,
          title: 'My jobs',
          subtitle: 'Work orders assigned to you, and closing them off.',
          onTap: () => context.go(MyJobsScreen.path),
        ),
      _ShortcutRow(
        icon: LucideIcons.fileText,
        title: 'My reports',
        subtitle: 'See where your reports have got to, and answer questions.',
        onTap: () => context.go(MyReportsScreen.path),
      ),
      if (role == Roles.reporter)
        _ShortcutRow(
          icon: LucideIcons.clipboardCheck,
          title: 'Confirm repairs',
          subtitle: 'Tell facilities whether a repair on something you reported has held.',
          onTap: () => context.go(PendingConfirmationsScreen.path),
        ),
      _ShortcutRow(
        icon: LucideIcons.scanQrCode,
        title: 'Scan an asset',
        subtitle: "Read a machine's QR sticker to see its history.",
        onTap: () => context.go(ScanAssetScreen.path),
      ),
    ];

    return MxCard(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Column(
        children: [
          for (var i = 0; i < rows.length; i++) ...[
            if (i > 0) const Divider(indent: 76, endIndent: 16),
            rows[i],
          ],
        ],
      ),
    );
  }
}

class _ShortcutRow extends StatelessWidget {
  const _ShortcutRow({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 14, 12),
        child: Row(
          children: [
            MxIconTile(icon: icon),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: theme.textTheme.titleMedium),
                  const SizedBox(height: 2),
                  Text(subtitle, style: theme.textTheme.bodySmall),
                ],
              ),
            ),
            const SizedBox(width: 8),
            const Icon(LucideIcons.chevronRight, size: 18, color: MxColors.mute),
          ],
        ),
      ),
    );
  }
}
