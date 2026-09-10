import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/providers.dart';
import 'auth_api.dart';
import 'auth_state.dart';

final authApiProvider = Provider<AuthApi>((ref) {
  return AuthApi(ref.watch(apiClientProvider));
});

final authControllerProvider =
    NotifierProvider<AuthController, AuthState>(AuthController.new);

/// App-wide session state. The token itself lives in [TokenStorage]; this holds the user.
class AuthController extends Notifier<AuthState> {
  @override
  AuthState build() {
    final storage = ref.watch(tokenStorageProvider);

    // The token can disappear without this controller asking — ApiClient clears it when
    // the API rejects it with a 401. Listening here is what turns that into a signed-out
    // session, which the router's redirect guard then acts on.
    void onTokenChanged() {
      if (storage.cachedToken == null && state.isAuthenticated) {
        state = const AuthState.signedOut(sessionExpired: true);
      }
    }

    storage.addListener(onTokenChanged);
    ref.onDispose(() => storage.removeListener(onTokenChanged));

    // Starts after build() returns; the app shows a spinner while status is `unknown`.
    unawaited(_restore());

    return const AuthState.unknown();
  }

  Future<void> _restore() async {
    final token = await ref.read(tokenStorageProvider).read();

    if (token == null) {
      state = const AuthState.signedOut();
      return;
    }

    try {
      state = AuthState.signedIn(await ref.read(authApiProvider).me());
    } on ApiException {
      // A 401 has already cleared the token. Anything else leaves the user signed out too,
      // because we could not confirm who they are.
      state = const AuthState.signedOut();
    }
  }

  /// Throws [ApiException] on failure so the login screen can show the message.
  Future<void> login(String email, String password) async {
    final result = await ref.read(authApiProvider).login(email, password);
    await ref.read(tokenStorageProvider).write(result.token);
    state = AuthState.signedIn(result.user);
  }

  /// No server call: the API issues an access token only, with nothing to revoke.
  Future<void> logout() async {
    // Set the state first: the token listener above treats a token disappearing while the
    // user is still signed in as an expiry, and a deliberate sign-out is not that.
    state = const AuthState.signedOut();
    await ref.read(tokenStorageProvider).clear();
  }

  void clearSessionExpired() {
    if (state.sessionExpired) {
      state = const AuthState.signedOut();
    }
  }
}