import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_controller.dart';
import '../features/auth/auth_state.dart';
import '../features/auth/login_screen.dart';
import '../features/home/home_screen.dart';
import '../features/reports/submit_report_screen.dart';

/// Bridges Riverpod to go_router: go_router re-runs its redirect when this notifies, and
/// it notifies whenever the sign-in status changes.
class _AuthRefresh extends ChangeNotifier {
  _AuthRefresh(Ref ref) {
    // The provider owns this listener, so it is torn down with the provider — there is
    // nothing to close by hand.
    ref.listen<AuthStatus>(
      authControllerProvider.select((state) => state.status),
      (_, __) => notifyListeners(),
    );
  }
}

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = _AuthRefresh(ref);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    initialLocation: HomeScreen.path,
    refreshListenable: refresh,

    // The guard. An unauthenticated user can reach exactly one screen: login. There is
    // no per-screen check to forget, because every route goes through here.
    //
    // `unknown` never reaches this point — MaintenXApp shows a spinner until secure
    // storage has been read, so a returning user is not flashed the login screen.
    redirect: (context, state) {
      final isAuthenticated = ref.read(authControllerProvider).isAuthenticated;
      final isAtLogin = state.matchedLocation == LoginScreen.path;

      if (!isAuthenticated && !isAtLogin) return LoginScreen.path;
      if (isAuthenticated && isAtLogin) return HomeScreen.path;
      return null;
    },
    routes: [
      GoRoute(
        path: LoginScreen.path,
        builder: (context, state) => const LoginScreen(),
      ),
      GoRoute(
        path: HomeScreen.path,
        builder: (context, state) => const HomeScreen(),
        routes: [
          GoRoute(
            path: SubmitReportScreen.subPath,
            builder: (context, state) => const SubmitReportScreen(),
          ),
        ],
      ),
    ],
  );
});
