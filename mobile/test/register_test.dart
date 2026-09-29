import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:maintenx_mobile/core/api_client.dart';
import 'package:maintenx_mobile/core/providers.dart';
import 'package:maintenx_mobile/core/token_storage.dart';
import 'package:maintenx_mobile/features/auth/auth_controller.dart';
import 'package:maintenx_mobile/features/auth/auth_state.dart';
import 'package:maintenx_mobile/features/auth/login_screen.dart';
import 'package:maintenx_mobile/features/auth/register_screen.dart';
import 'package:maintenx_mobile/router/app_router.dart';

/// A token store that keeps the token in memory — a test has no keychain.
class _MemoryTokenStorage extends TokenStorage {
  _MemoryTokenStorage() : super(const FlutterSecureStorage());

  String? _token;

  @override
  Future<String?> read() async => _token;

  @override
  String? get cachedToken => _token;

  @override
  Future<void> write(String token) async {
    _token = token;
    notifyListeners();
  }

  @override
  Future<void> clear() async {
    _token = null;
    notifyListeners();
  }
}

/// A phone-height surface: the form's button sits below an 800×600 test window's fold.
void _tallScreen(WidgetTester tester) {
  tester.view.physicalSize = const Size(430, 1400);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(tester.view.reset);
}

http.Response _json(Object body, [int status = 200]) =>
    http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});

void main() {
  group('validateRegistration', () {
    Map<String, String> validate({
      String fullName = 'Nimal Perera',
      String email = 'nimal@campus.test',
      String password = 'LongEnough1',
      String? confirmPassword,
    }) =>
        validateRegistration(
          fullName: fullName,
          email: email,
          password: password,
          confirmPassword: confirmPassword ?? password,
        );

    test('a complete form is valid', () {
      expect(validate(), isEmpty);
    });

    test('every field is required', () {
      final errors = validate(fullName: ' ', email: '', password: '', confirmPassword: '');
      expect(errors.keys, containsAll(['fullName', 'email', 'password']));
    });

    test('the email must look like one', () {
      expect(validate(email: 'not-an-email')['email'], 'Enter a valid email address.');
    });

    test("the password follows the API's 8 to 128 characters", () {
      expect(validate(password: 'short1')['password'], isNotNull);
      expect(validate(password: 'x' * 129)['password'], isNotNull);
    });

    test('the confirmation must match', () {
      expect(validate(confirmPassword: 'Different1')['confirmPassword'], 'The passwords do not match.');
    });
  });

  group('RegisterScreen', () {
    late List<http.Request> requests;

    Future<ProviderContainer> pump(WidgetTester tester, http.Response Function(http.Request) respond) async {
      _tallScreen(tester);
      requests = [];
      final storage = _MemoryTokenStorage();
      final client = ApiClient(
        baseUrl: 'http://api.test',
        tokenStorage: storage,
        httpClient: MockClient((request) async {
          requests.add(request);
          return respond(request);
        }),
      );
      final container = ProviderContainer(overrides: [
        tokenStorageProvider.overrideWithValue(storage),
        apiClientProvider.overrideWithValue(client),
      ]);
      addTearDown(container.dispose);

      await tester.pumpWidget(UncontrolledProviderScope(
        container: container,
        child: const MaterialApp(home: RegisterScreen()),
      ));
      await tester.pumpAndSettle();
      return container;
    }

    Future<void> fillAndSubmit(WidgetTester tester) async {
      final fields = find.byType(TextField);
      await tester.enterText(fields.at(0), 'Nimal Perera');
      await tester.enterText(fields.at(1), 'nimal@campus.test');
      await tester.enterText(fields.at(2), 'LongEnough1');
      await tester.enterText(fields.at(3), 'LongEnough1');
      await tester.ensureVisible(find.text('Create account'));
      await tester.tap(find.text('Create account'));
      await tester.pumpAndSettle();
    }

    testWidgets('signs up WITHOUT asking for a role, and is signed in by the reply', (tester) async {
      final container = await pump(
        tester,
        (_) => _json({
          'token': 'new-token',
          'userId': 31,
          'email': 'nimal@campus.test',
          'fullName': 'Nimal Perera',
          'role': 'Reporter',
        }, 201),
      );

      await fillAndSubmit(tester);

      final register = requests.singleWhere((r) => r.url.path == '/api/auth/register');
      final body = jsonDecode(register.body) as Map<String, dynamic>;
      expect(body, {'fullName': 'Nimal Perera', 'email': 'nimal@campus.test', 'password': 'LongEnough1'});
      expect(body.containsKey('role'), isFalse);

      final state = container.read(authControllerProvider);
      expect(state.isAuthenticated, isTrue);
      expect(state.user?.role, Roles.reporter);
    });

    testWidgets('an email already registered is shown as the API says it', (tester) async {
      await pump(
        tester,
        (request) => request.url.path == '/api/auth/register'
            ? _json({'status': 409, 'title': 'Email already registered.',
                'detail': 'An account with that email address already exists.'}, 409)
            : _json({}, 404),
      );

      await fillAndSubmit(tester);

      expect(find.text('Could not create the account'), findsOneWidget);
      expect(find.text('An account with that email address already exists.'), findsOneWidget);
    });

    testWidgets('an invalid form is not sent', (tester) async {
      await pump(tester, (_) => _json({}, 500));

      await tester.ensureVisible(find.text('Create account'));
      await tester.tap(find.text('Create account'));
      await tester.pumpAndSettle();

      expect(requests.where((r) => r.url.path == '/api/auth/register'), isEmpty);
      expect(find.text('Your name is required.'), findsOneWidget);
    });
  });

  testWidgets('signed out, the guard lets a visitor reach the registration screen from sign-in',
      (tester) async {
    _tallScreen(tester);
    final storage = _MemoryTokenStorage();
    final container = ProviderContainer(overrides: [
      tokenStorageProvider.overrideWithValue(storage),
      apiClientProvider.overrideWithValue(ApiClient(
        baseUrl: 'http://api.test',
        tokenStorage: storage,
        httpClient: MockClient((_) async => _json({}, 404)),
      )),
    ]);
    addTearDown(container.dispose);

    await tester.pumpWidget(UncontrolledProviderScope(
      container: container,
      child: Consumer(
        builder: (context, ref, _) => MaterialApp.router(routerConfig: ref.watch(routerProvider)),
      ),
    ));
    await tester.pumpAndSettle();

    expect(find.byType(LoginScreen), findsOneWidget);

    await tester.ensureVisible(find.text('New here? Create an account to report faults'));
    await tester.tap(find.text('New here? Create an account to report faults'));
    await tester.pumpAndSettle();

    expect(find.byType(RegisterScreen), findsOneWidget);
  });
}
