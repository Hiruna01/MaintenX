import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:maintenx_mobile/core/api_client.dart';
import 'package:maintenx_mobile/core/providers.dart';
import 'package:maintenx_mobile/core/token_storage.dart';
import 'package:maintenx_mobile/features/assets/asset.dart';
import 'package:maintenx_mobile/features/assets/asset_detail_screen.dart';
import 'package:maintenx_mobile/features/assets/assets_api.dart';
import 'package:maintenx_mobile/features/assets/scan_asset_screen.dart';
import 'package:maintenx_mobile/widgets/error_view.dart';

/// A signed-in token store that never touches the platform keychain.
class _FakeTokenStorage extends TokenStorage {
  _FakeTokenStorage() : super(const FlutterSecureStorage());

  @override
  Future<String?> read() async => 'test-token';
}

ApiClient _client(MockClientHandler handler) => ApiClient(
      baseUrl: 'http://api.test',
      tokenStorage: _FakeTokenStorage(),
      httpClient: MockClient(handler),
    );

http.Response _json(Object body, [int status = 200]) => http.Response(
      jsonEncode(body),
      status,
      headers: {'content-type': 'application/json'},
    );

/// The seeded repeat-failure projector, notes verbatim from DbSeeder.
Map<String, dynamic> _projector({String? warrantyExpiresOn = '2025-02-10', List<Object>? history}) => {
      'id': 1,
      'assetTag': 'PRJ-MAB101-01',
      'name': 'Lecture Hall A Projector',
      'category': {'id': 2, 'name': 'Projector', 'defaultWarrantyMonths': 24},
      'room': {'id': 1, 'buildingId': 1, 'name': 'Lecture Hall A', 'code': 'MAB-101', 'floor': 1},
      'manufacturer': 'Epson',
      'model': 'EB-L630U',
      'installedOn': '2023-02-10',
      'warrantyExpiresOn': warrantyExpiresOn,
      'status': 'Active',
      'serviceHistory': history ??
          [
            {
              'id': 1,
              'assetId': 1,
              'servicedOn': '2026-05-12',
              'technicianName': 'K. Perera',
              'technicianNote': 'projector cutting out mid lecture. checked hdmi + cable, '
                  'reseated both. ran 20min on test, no fault seen. adv. dept to report '
                  'again if recurs.',
              'outcome': 'NoFaultFound',
              'workOrderId': null,
            },
            {
              'id': 3,
              'assetId': 1,
              'servicedOn': '2026-09-02',
              'technicianName': 'S. Fernando',
              'technicianNote': 'cleaned filter, unit still running hot, temporary fix, fan '
                  'bearing sounds weak - recommend replacement before next term',
              'outcome': 'TemporaryFix',
              'workOrderId': 12,
            },
          ],
    };

Map<String, dynamic> _summary({bool underWarranty = false, bool repeat = true}) => {
      'assetId': 1,
      'assetTag': 'PRJ-MAB101-01',
      'failureCount12Months': 3,
      'failureCount3Months': 3,
      'lastServicedOn': '2026-09-02',
      'daysSinceLastService': 21,
      'temporaryFixCount': 2,
      'isUnderWarranty': underWarranty,
      'isRepeatFailure': repeat,
      'distinctOutcomes': ['TemporaryFix', 'NoFaultFound'],
    };

void main() {
  group('AssetsApi.findByTag', () {
    test('returns the asset for a registered tag, on the by-tag path', () async {
      late Uri requested;
      final api = AssetsApi(_client((request) async {
        requested = request.url;
        return _json(_projector());
      }));

      final asset = await api.findByTag('PRJ-MAB101-01');

      expect(requested.path, '/api/assets/by-tag/PRJ-MAB101-01');
      expect(asset!.assetTag, 'PRJ-MAB101-01');
      expect(asset.room.code, 'MAB-101');
    });

    test('an unknown tag is null, not an exception — the scanner shows "not registered"', () async {
      final api = AssetsApi(_client((_) async => http.Response('', 404)));

      expect(await api.findByTag('SOMEONE-ELSES-STICKER'), isNull);
    });

    test('a URL in a QR code is path-encoded rather than split into segments', () async {
      late Uri requested;
      final api = AssetsApi(_client((request) async {
        requested = request.url;
        return http.Response('', 404);
      }));

      await api.findByTag('https://example.com/a/b');

      expect(requested.pathSegments.length, 4);
      expect(requested.pathSegments.last, 'https://example.com/a/b');
    });

    test('no network still throws, so it is never mistaken for "not registered"', () async {
      final api = AssetsApi(_client((_) async => throw http.ClientException('offline')));

      expect(
        () => api.findByTag('PRJ-MAB101-01'),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'statusCode', 0)),
      );
    });
  });

  group('AssetDetail.fromJson', () {
    test('keeps the service history in the order the API sent it (oldest first)', () {
      final asset = AssetDetail.fromJson(_projector());

      expect(asset.serviceHistory.map((r) => r.servicedOn), ['2026-05-12', '2026-09-02']);
      expect(asset.serviceHistory.first.workOrderId, isNull);
    });
  });

  test('formatDateOnly reads the calendar date without a timezone conversion', () {
    expect(formatDateOnly('2026-07-03'), '3 Jul 2026');
    expect(formatDateOnly(null), '—');
    expect(formatDateOnly('not a date'), '—');
  });

  test('enums are matched by name, never by ordinal', () {
    expect(AssetStatuses.underMaintenance, 'UnderMaintenance');
    expect(AssetStatuses.label(AssetStatuses.underMaintenance), 'Under Maintenance');
    expect(ServiceOutcomes.label(ServiceOutcomes.noFaultFound), 'No Fault Found');
  });

  group('AssetDetailScreen', () {
    Future<void> pump(
      WidgetTester tester, {
      required Map<String, dynamic> asset,
      http.Response Function()? summary,
    }) async {
      // Tall enough that the lazily built list lays out every visit.
      tester.view.physicalSize = const Size(800, 4000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final client = _client((request) async {
        if (request.url.path.endsWith('/failure-summary')) {
          return summary?.call() ?? _json(_summary());
        }
        return _json(asset);
      });

      await tester.pumpWidget(
        ProviderScope(
          overrides: [apiClientProvider.overrideWithValue(client)],
          child: const MaterialApp(home: AssetDetailScreen(assetId: 1)),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('shows every technician note verbatim, and the repeat-failure flag',
        (tester) async {
      await pump(tester, asset: _projector());

      expect(find.text('Lecture Hall A Projector'), findsOneWidget);
      expect(find.text('Repeat failure'), findsOneWidget);
      // The whole note, not a truncated first line.
      expect(
        find.text('cleaned filter, unit still running hot, temporary fix, fan bearing sounds '
            'weak - recommend replacement before next term'),
        findsOneWidget,
      );
      expect(find.text('Visit 1 of 2'), findsOneWidget);
      expect(find.text('S. Fernando · Work order #12'), findsOneWidget);
    });

    testWidgets('the warranty chip is the API answer: expired reads grey with its date',
        (tester) async {
      await pump(tester, asset: _projector());
      expect(find.text('Warranty expired · 10 Feb 2025'), findsOneWidget);
    });

    testWidgets('under warranty reads green with its date', (tester) async {
      await pump(
        tester,
        asset: _projector(warrantyExpiresOn: '2027-01-15'),
        summary: () => _json(_summary(underWarranty: true, repeat: false)),
      );
      expect(find.text('Under warranty · until 15 Jan 2027'), findsOneWidget);
      expect(find.text('Repeat failure'), findsNothing);
    });

    testWidgets('a failed summary does not hide the history', (tester) async {
      await pump(tester, asset: _projector(), summary: () => http.Response('', 500));

      expect(find.text('Warranty status unavailable'), findsOneWidget);
      expect(find.textContaining('Failure summary unavailable'), findsOneWidget);
      expect(find.text('Visit 2 of 2'), findsOneWidget);
    });

    testWidgets('an empty history is an empty state, not an error', (tester) async {
      await pump(tester, asset: _projector(history: const []));
      expect(find.text('No service visits on record.'), findsOneWidget);
    });
  });

  // Driven through the typed-tag fallback: a test has no camera — which is itself the
  // "camera could not start" case — and a typed tag goes down the same by-tag lookup a
  // scan does.
  group('ScanAssetScreen', () {
    const cameraChannel = MethodChannel('dev.steenbakker.mobile_scanner/scanner/method');
    var permissionRequests = 0;

    // Stands in for the camera plugin: the user has refused camera access, and refuses
    // again when asked. This is exactly the state a demo hits after one wrong tap on the
    // permission prompt.
    setUp(() {
      permissionRequests = 0;
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(cameraChannel, (call) async {
        switch (call.method) {
          case 'state':
            return 2; // MobileScannerAuthorizationState.denied
          case 'request':
            permissionRequests++;
            return false;
          default:
            return null;
        }
      });
    });

    tearDown(() {
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(cameraChannel, null);
    });

    Future<void> lookUpTyped(WidgetTester tester, MockClientHandler handler, String tag) async {
      await tester.pumpWidget(
        ProviderScope(
          overrides: [apiClientProvider.overrideWithValue(_client(handler))],
          child: const MaterialApp(home: ScanAssetScreen()),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byTooltip('Type the tag instead'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), tag);
      await tester.tap(find.text('Look up'));
      await tester.pumpAndSettle();
    }

    testWidgets('camera permission denied is explained, with a retry that asks again',
        (tester) async {
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            apiClientProvider.overrideWithValue(_client((_) async => http.Response('', 404))),
          ],
          child: const MaterialApp(home: ScanAssetScreen()),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Camera access is off'), findsOneWidget);
      expect(find.text('Type the tag instead'), findsOneWidget);
      // The viewfinder is not drawn over the message.
      expect(find.text("Point the camera at an asset's QR sticker"), findsNothing);

      final before = permissionRequests;
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(permissionRequests, before + 1);
      expect(find.text('Camera access is off'), findsOneWidget);
    });

    testWidgets('an unknown tag reads "No asset registered for this code"', (tester) async {
      await lookUpTyped(tester, (_) async => http.Response('', 404), 'NOT-OURS-01');

      expect(find.text('No asset registered for this code'), findsOneWidget);
      expect(find.text('NOT-OURS-01'), findsOneWidget);
      expect(find.text('Scan again'), findsOneWidget);
    });

    testWidgets('no network is an ErrorView with a retry, not "not registered"', (tester) async {
      await lookUpTyped(
        tester,
        (_) async => throw http.ClientException('offline'),
        'PRJ-MAB101-01',
      );

      expect(find.byType(ErrorView), findsOneWidget);
      expect(find.text('Could not reach the API.'), findsOneWidget);
      expect(find.text('No asset registered for this code'), findsNothing);
    });

    testWidgets('an empty typed tag is refused by validate(), not sent', (tester) async {
      var calls = 0;
      await lookUpTyped(tester, (_) async {
        calls++;
        return http.Response('', 404);
      }, '   ');

      expect(find.text('Enter the tag printed under the QR code.'), findsOneWidget);
      expect(calls, 0);
    });
  });
}
