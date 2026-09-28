import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/app_theme.dart';
import 'features/auth/auth_controller.dart';
import 'features/auth/auth_state.dart';
import 'router/app_router.dart';
import 'widgets/surfaces.dart';

void main() {
  runApp(const ProviderScope(child: MaintenXApp()));
}

class MaintenXApp extends ConsumerWidget {
  const MaintenXApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final status = ref.watch(authControllerProvider.select((state) => state.status));

    final theme = AppTheme.light();

    // While secure storage is being read we cannot know which screen is correct, so show a
    // spinner rather than guessing and flashing the wrong one.
    if (status == AuthStatus.unknown) {
      return MaterialApp(
        title: 'MaintenX',
        debugShowCheckedModeBanner: false,
        theme: theme,
        home: const _Splash(),
      );
    }

    return MaterialApp.router(
      title: 'MaintenX',
      debugShowCheckedModeBanner: false,
      theme: theme,
      routerConfig: ref.watch(routerProvider),
    );
  }
}

/// The first frame a returning user sees while their stored token is read: the mark, alone,
/// on white — the same white the sign-in screen opens on, so nothing flashes.
class _Splash extends StatelessWidget {
  const _Splash();

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      backgroundColor: MxColors.surface,
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            MxBrandMark(size: 52),
            SizedBox(height: 28),
            SizedBox(
              width: 18,
              height: 18,
              child: CircularProgressIndicator(strokeWidth: 2, strokeCap: StrokeCap.round),
            ),
            SizedBox(height: 12),
            Text('Starting…', style: TextStyle(color: MxColors.graphite)),
          ],
        ),
      ),
    );
  }
}
