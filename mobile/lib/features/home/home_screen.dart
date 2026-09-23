import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../assets/scan_asset_screen.dart';
import '../auth/auth_controller.dart';
import '../auth/auth_state.dart';
import '../reports/my_reports_screen.dart';
import '../reports/submit_report_screen.dart';

/// The landing screen: a card per thing the app can do. Assigned work orders arrive with
/// that feature.
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
