import 'package:flutter_test/flutter_test.dart';
import 'package:maintenx_mobile/features/auth/auth_state.dart';

void main() {
  group('AuthUser.fromJson', () {
    test('reads the login response, where the id is named "userId"', () {
      final user = AuthUser.fromJson(const {
        'token': 'ignored-here',
        'userId': 7,
        'email': 'manager@campus.test',
        'fullName': 'Demo Facilities Manager',
        'role': 'FacilitiesManager',
      });

      expect(user.id, 7);
      expect(user.role, Roles.facilitiesManager);
    });

    test('reads GET /api/auth/me, where the same id is named "id"', () {
      final user = AuthUser.fromJson(const {
        'id': 7,
        'email': 'manager@campus.test',
        'fullName': 'Demo Facilities Manager',
        'role': 'FacilitiesManager',
      });

      expect(user.id, 7);
    });
  });

  test('roles are matched by name, never by ordinal', () {
    expect(Roles.facilitiesManager, 'FacilitiesManager');
    expect(Roles.label(Roles.facilitiesManager), 'Facilities Manager');
    expect(Roles.label(null), 'Unknown');
  });
}
