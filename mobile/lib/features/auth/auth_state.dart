/// Mirrors the API's `Role` enum. The API serialises enums by NAME on both sides, so these
/// are the exact strings that travel in JSON and sit in the `role` claim of the JWT — a
/// client never hardcodes an enum's integer value.
class Roles {
  const Roles._();

  static const String reporter = 'Reporter';
  static const String technician = 'Technician';
  static const String facilitiesManager = 'FacilitiesManager';
  static const String admin = 'Admin';

  static String label(String? role) {
    switch (role) {
      case reporter:
        return 'Reporter';
      case technician:
        return 'Technician';
      case facilitiesManager:
        return 'Facilities Manager';
      case admin:
        return 'Admin';
      default:
        return 'Unknown';
    }
  }
}

class AuthUser {
  const AuthUser({
    required this.id,
    required this.email,
    required this.fullName,
    required this.role,
  });

  final int id;
  final String email;
  final String fullName;
  final String role;

  /// Handles both AuthResponse (`userId`) and UserDto (`id`).
  factory AuthUser.fromJson(Map<String, dynamic> json) {
    return AuthUser(
      id: (json['id'] ?? json['userId']) as int,
      email: json['email'] as String,
      fullName: json['fullName'] as String,
      role: json['role'] as String,
    );
  }
}

/// `unknown` is the startup state, while secure storage is being read. The app shows a
/// spinner then, so a returning user is never flashed the login screen before their stored
/// token has had a chance to be checked.
enum AuthStatus { unknown, authenticated, unauthenticated }

class AuthState {
  const AuthState({
    required this.status,
    this.user,
    this.sessionExpired = false,
  });

  const AuthState.unknown() : this(status: AuthStatus.unknown);

  const AuthState.signedOut({bool sessionExpired = false})
      : this(status: AuthStatus.unauthenticated, sessionExpired: sessionExpired);

  const AuthState.signedIn(AuthUser user)
      : this(status: AuthStatus.authenticated, user: user);

  final AuthStatus status;
  final AuthUser? user;

  /// True when the session ended because the API rejected the token, not because the user
  /// pressed sign out. The login screen shows a notice for it.
  final bool sessionExpired;

  bool get isAuthenticated => status == AuthStatus.authenticated;
}
