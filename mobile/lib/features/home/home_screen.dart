import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../assets/scan_asset_screen.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_state.dart';
import '../reports/my_reports_screen.dart';
import '../reports/submit_report_screen.dart';
import '../verification/pending_confirmations_screen.dart';
import '../workorders/my_jobs_screen.dart';

/// The landing screen: a card per thing the app can do. Navigation is role-based — "My
/// jobs" is offered to a Technician only, since the API gives nobody else a queue of their
/// own there, and "Confirm repairs" to a Reporter only: the API takes a repair's
/// confirmation from the reporter who filed the fault and nobody else, and a manager's list
/// there would be every reporter's checks, none of them theirs to answer.
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  static const String path = '/';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider.select((state) => state.user));

    return Scaffold(
      appBar: AppBar(
        title: const Text('MaintenX'),
        actions: [
          IconButton(
            tooltip: 'Sign out',
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(authControllerProvider.notifier).logout(),
          ),
        ],
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            Text('Home', style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 4),
            if (user != null)
              Text(
                'Signed in as ${user.fullName} (${Roles.label(user.role)}).',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            const SizedBox(height: 24),
            if (user?.role == Roles.technician) ...[
              Card(
                child: ListTile(
                  leading: const Icon(Icons.handyman_outlined),
                  title: const Text('My jobs'),
                  subtitle: const Text('Work orders assigned to you, and closing them off.'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => context.go(MyJobsScreen.path),
                ),
              ),
              const SizedBox(height: 8),
            ],
            Card(
              child: ListTile(
                leading: const Icon(Icons.report_outlined),
                title: const Text('Submit a report'),
                subtitle: const Text('Tell facilities what needs fixing.'),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => context.go(SubmitReportScreen.path),
              ),
            ),
            const SizedBox(height: 8),
            Card(
              child: ListTile(
                leading: const Icon(Icons.list_alt_outlined),
                title: const Text('My reports'),
                subtitle: const Text('See where your reports have got to, and answer questions.'),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => context.go(MyReportsScreen.path),
              ),
            ),
            const SizedBox(height: 8),
            if (user?.role == Roles.reporter) ...[
              Card(
                child: ListTile(
                  leading: const Icon(Icons.fact_check_outlined),
                  title: const Text('Confirm repairs'),
                  subtitle: const Text('Tell facilities whether a repair on something you '
                      'reported has held.'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => context.go(PendingConfirmationsScreen.path),
                ),
              ),
              const SizedBox(height: 8),
            ],
            Card(
              child: ListTile(
                leading: const Icon(Icons.qr_code_scanner),
                title: const Text('Scan an asset'),
                subtitle: const Text("Read a machine's QR sticker to see its history."),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => context.go(ScanAssetScreen.path),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
