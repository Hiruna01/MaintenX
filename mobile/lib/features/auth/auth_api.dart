import '../../core/api_client.dart';
import 'auth_state.dart';

/// Every call the auth feature makes to the API. Screens never call the client directly.
class AuthApi {
  AuthApi(this._client);

  final ApiClient _client;

  /// POST /api/auth/login -> 200 with { token, userId, email, fullName, role }, or 401.
  Future<({String token, AuthUser user})> login(String email, String password) async {
    final json = await _client.post(
      '/api/auth/login',
      body: {'email': email, 'password': password},
    ) as Map<String, dynamic>;

    return (token: json['token'] as String, user: AuthUser.fromJson(json));
  }

  /// GET /api/auth/me -> 200 with { id, email, fullName, role }. Used to restore a session.
  Future<AuthUser> me() async {
    final json = await _client.get('/api/auth/me') as Map<String, dynamic>;
    return AuthUser.fromJson(json);
  }
}
