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

  /// POST /api/auth/register -> 201 with the same body as login, signing the new account in.
  ///
  /// Sends NO role. Registration from the phone is for reporters: the API makes every
  /// anonymous registration a Reporter and refuses (403) any other role unless an Admin asks,
  /// so there is nothing here that could ask for more. 409 when the email is already taken.
  Future<({String token, AuthUser user})> register({
    required String fullName,
    required String email,
    required String password,
  }) async {
    final json = await _client.post(
      '/api/auth/register',
      body: {'fullName': fullName, 'email': email, 'password': password},
    ) as Map<String, dynamic>;

    return (token: json['token'] as String, user: AuthUser.fromJson(json));
  }

  /// GET /api/auth/me -> 200 with { id, email, fullName, role }. Used to restore a session.
  Future<AuthUser> me() async {
    final json = await _client.get('/api/auth/me') as Map<String, dynamic>;
    return AuthUser.fromJson(json);
  }
}
