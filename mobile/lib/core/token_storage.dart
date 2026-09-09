import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Where the access token lives.
///
/// **flutter_secure_storage, never SharedPreferences.** SharedPreferences is an
/// unencrypted XML file on Android and a plist on iOS, sitting in the app sandbox:
/// readable on a rooted or jailbroken device, and extractable from an `adb backup`. This
/// wrapper keeps the token in the iOS Keychain and Android's EncryptedSharedPreferences
/// instead, which is the whole reason the dependency is here.
///
/// The API issues one access token with a 12-hour lifetime and no refresh token, so there
/// is exactly one value to store and nothing to rotate.
///
/// It is a [ChangeNotifier] so that clearing the token — including when [ApiClient] clears
/// it after a 401 — reaches the auth controller without the client knowing anything about
/// screens or navigation.
class TokenStorage extends ChangeNotifier {
  TokenStorage(this._storage);

  static const String _tokenKey = 'maintenx.token';

  final FlutterSecureStorage _storage;

  String? _cachedToken;
  bool _loaded = false;

  /// Cached after the first read, so the hot path of every request is not a platform call.
  Future<String?> read() async {
    if (!_loaded) {
      _cachedToken = await _storage.read(key: _tokenKey);
      _loaded = true;
    }
    return _cachedToken;
  }

  String? get cachedToken => _cachedToken;

  Future<void> write(String token) async {
    _cachedToken = token;
    _loaded = true;
    await _storage.write(key: _tokenKey, value: token);
    notifyListeners();
  }

  Future<void> clear() async {
    _cachedToken = null;
    _loaded = true;
    await _storage.delete(key: _tokenKey);
    notifyListeners();
  }
}
