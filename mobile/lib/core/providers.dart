import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'api_client.dart';
import 'env.dart';
import 'token_storage.dart';

final secureStorageProvider = Provider<FlutterSecureStorage>((ref) {
  return const FlutterSecureStorage(
    aOptions: AndroidOptions(encryptedSharedPreferences: true),
    iOptions: IOSOptions(accessibility: KeychainAccessibility.first_unlock),
  );
});

final tokenStorageProvider = Provider<TokenStorage>((ref) {
  final storage = TokenStorage(ref.watch(secureStorageProvider));
  ref.onDispose(storage.dispose);
  return storage;
});

/// The single API client for the app. Depends only on the token store — it has no idea
/// that auth state or a router exist.
final apiClientProvider = Provider<ApiClient>((ref) {
  final client = ApiClient(
    baseUrl: Env.apiBaseUrl,
    tokenStorage: ref.watch(tokenStorageProvider),
  );
  ref.onDispose(client.dispose);
  return client;
});
