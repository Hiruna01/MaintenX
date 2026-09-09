import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'features/auth/auth_controller.dart';
import 'features/auth/auth_state.dart';
import 'router/app_router.dart';
import 'widgets/loading_view.dart';

void main() {
  runApp(const ProviderScope(child: MaintenXApp()));
}

class MaintenXApp extends ConsumerWidget {
  const MaintenXApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final status = ref.watch(authControllerProvider.select((state) => state.status));

    final theme = ThemeData(
      colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF1D4ED8)),
      useMaterial3: true,
    );

    // While secure storage is being read we cannot know which screen is correct, so show a
    // spinner rather than guessing and flashing the wrong one.
    if (status == AuthStatus.unknown) {
      return MaterialApp(
        title: 'MaintenX',
        theme: theme,
        home: const Scaffold(body: LoadingView(message: 'Starting…')),
      );
    }

    return MaterialApp.router(
      title: 'MaintenX',
      theme: theme,
      routerConfig: ref.watch(routerProvider),
    );
  }
}
