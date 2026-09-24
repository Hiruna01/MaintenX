import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:image_picker/image_picker.dart';
import 'package:maintenx_mobile/core/api_client.dart';
import 'package:maintenx_mobile/core/providers.dart';
import 'package:maintenx_mobile/core/token_storage.dart';
import 'package:maintenx_mobile/features/reports/report_photo.dart';
import 'package:maintenx_mobile/features/workorders/complete_job_screen.dart';
import 'package:maintenx_mobile/features/workorders/completion.dart';
import 'package:maintenx_mobile/features/workorders/job_detail_screen.dart';
import 'package:maintenx_mobile/features/workorders/my_jobs_screen.dart';
import 'package:maintenx_mobile/features/workorders/work_order.dart';
import 'package:maintenx_mobile/features/workorders/work_orders_api.dart';
import 'package:maintenx_mobile/widgets/empty_view.dart';
import 'package:maintenx_mobile/widgets/error_view.dart';

/// A signed-in token store that never touches the platform keychain.
class _FakeTokenStorage extends TokenStorage {
  _FakeTokenStorage() : super(const FlutterSecureStorage());

  @override
  Future<String?> read() async => 'test-token';
}

/// Stands in for the camera and gallery, which a test does not have.
class _FakePicker extends ImagePicker {
  _FakePicker(this._pick);

  final Future<XFile?> Function() _pick;

  @override
  Future<XFile?> pickImage({
    required ImageSource source,
    double? maxWidth,
    double? maxHeight,
    int? imageQuality,
    CameraDevice preferredCameraDevice = CameraDevice.rear,
    bool requestFullMetadata = true,
  }) =>
      _pick();
}

const int _me = 11;

/// A note that clears the 20-character floor, written the way technicians write them.
const String _note = 'fan bearing worn, replaced fan + cleaned filter. ran 30min, no cutout.';

ApiClient _client(MockClientHandler handler) => ApiClient(
      baseUrl: 'http://api.test',
      tokenStorage: _FakeTokenStorage(),
      httpClient: MockClient(handler),
    );

http.Response _json(Object? body, [int status = 200]) => http.Response(
      jsonEncode(body),
      status,
      headers: {'content-type': 'application/json'},
    );

http.Response _problem(int status, String detail) =>
    _json({'status': status, 'title': 'Problem', 'detail': detail}, status);

Map<String, dynamic> _row(int id, {String status = 'Scheduled'}) => {
      'id': id,
      'reportId': 5,
      'assetId': 3,
      'assetTag': 'PRJ-MAB101-01',
      'assignedTechnicianId': _me,
      'assignedTechnicianName': 'Nimal Perera',
      'status': status,
      'strategy': 'EscalateReplacement',
      'estimatedCost': 45000,
      'actualCost': null,
      'completedAt': null,
      'createdAt': '2026-09-20T07:00:00Z',
      'updatedAt': '2026-09-20T07:00:00Z',
    };

Map<String, dynamic> _page(List<Object> items) => {
      'items': items,
      'page': 1,
      'pageSize': 20,
      'totalCount': items.length,
      'totalPages': 1,
    };

const Map<String, dynamic> _readableDiagnosis = {
  'stepId': 40,
  'workflowId': 9,
  'recordedAt': '2026-09-20T07:05:00Z',
  'validationResult': 'Ok',
  'errorMessage': null,
  'outputReadable': true,
  'hypotheses': [
    {
      'cause': 'Overheating due to a failing cooling fan',
      'confidence': 'high',
      'evidence': ['2026-09-02: fan bearing weak, temporary fix.'],
    },
  ],
  'primaryHypothesisIndex': 0,
  'recommendedNextAction': 'replace',
  'reasoningSummary': 'Cleaning is not holding.',
};

Map<String, dynamic> _detail({
  int id = 7,
  String status = 'Scheduled',
  int? technicianId = _me,
  String? parts = 'Cooling fan 80mm, thermal paste',
  List<Object> slots = const [
    {
      'id': 1,
      'workOrderId': 7,
      'startsAt': '2026-09-28T03:30:00Z',
      'endsAt': '2026-09-28T05:00:00Z',
      'createdAt': '2026-09-20T07:00:00Z',
      'updatedAt': '2026-09-20T07:00:00Z',
    },
  ],
  Map<String, dynamic>? diagnosis = _readableDiagnosis,
}) =>
    {
      'id': id,
      'reportId': 5,
      'reportDescription': 'Projector cuts out 20 min into every lecture.\nFan very loud.',
      'asset': {
        'id': 3,
        'assetTag': 'PRJ-MAB101-01',
        'name': 'Ceiling Projector',
        'assetCategoryId': 1,
        'roomId': 2,
        'manufacturer': 'Epson',
        'model': 'EB-L630U',
        'installedOn': '2023-01-10',
        'warrantyExpiresOn': null,
        'status': 'Active',
        'createdAt': '2026-01-01T00:00:00Z',
        'updatedAt': '2026-01-01T00:00:00Z',
      },
      'room': {
        'id': 2,
        'buildingId': 1,
        'name': 'Lecture Hall A',
        'code': 'MAB101',
        'floor': 1,
        'createdAt': '2026-01-01T00:00:00Z',
        'updatedAt': '2026-01-01T00:00:00Z',
      },
      'assignedTechnician': technicianId == null
          ? null
          : {'id': technicianId, 'email': 't@campus.test', 'fullName': 'Nimal Perera', 'role': 'Technician'},
      'status': status,
      'strategy': 'EscalateReplacement',
      'estimatedCost': 45000,
      'actualCost': null,
      'approvalBasis': {
        'threshold': 15000,
        'exceedsThreshold': true,
        'isReplacement': true,
        'requiresApproval': true,
      },
      'partsRequired': parts,
      'resolutionNote': null,
      'completionPhotoUrl': null,
      'approvedBy': null,
      'approvedAt': null,
      'rejectionReason': null,
      'revisionNote': null,
      'completedAt': null,
      'createdAt': '2026-09-20T07:00:00Z',
      'updatedAt': '2026-09-20T07:00:00Z',
      'scheduledSlots': slots,
      'diagnosis': diagnosis,
    };

/// A JPEG's signature followed by filler.
Uint8List _jpeg() => Uint8List(4096)..setAll(0, const [0xFF, 0xD8, 0xFF]);

/// The screen under test at [initialLocation], with every place it can navigate to a
/// labelled stub — `context.go` needs a real router.
Future<void> _pumpRouted(
  WidgetTester tester, {
  required MockClientHandler handler,
  required String initialLocation,
  required List<RouteBase> routes,
  ImagePicker? picker,
}) async {
  tester.view.physicalSize = const Size(800, 2400);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);

  final router = GoRouter(
    initialLocation: initialLocation,
    routes: [
      ...routes,
      GoRoute(path: '/', builder: (_, __) => const Text('HOME')),
    ],
  );
  addTearDown(router.dispose);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        apiClientProvider.overrideWithValue(_client(handler)),
        currentUserIdProvider.overrideWithValue(_me),
        if (picker != null) imagePickerProvider.overrideWithValue(picker),
      ],
      child: MaterialApp.router(routerConfig: router),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('validateCompletion', () {
    Map<String, String> validate({
      String? outcome = 'Resolved',
      String cost = '1500',
      String note = _note,
    }) =>
        validateCompletion(outcome: outcome, actualCost: cost, resolutionNote: note);

    test('"done" is refused — it teaches the diagnostic agent nothing', () {
      expect(validate(note: 'done'), contains('resolutionNote'));
    });

    test('the floor is exactly 20 characters, counted after trimming', () {
      expect(validate(note: 'x' * 19), contains('resolutionNote'));
      expect(validate(note: 'x' * 20), isEmpty);
      expect(validate(note: '   ${'x' * 19}   '), contains('resolutionNote'));
      expect(validate(note: ' ' * 30), contains('resolutionNote'));
    });

    test('the note is capped at 2000', () {
      expect(validate(note: 'x' * 2001), contains('resolutionNote'));
    });

    test('there is no default outcome', () {
      expect(validate(outcome: null), {'outcome': anything});
    });

    test('the cost is rupees and at most two decimals, within the API range', () {
      expect(validate(cost: ''), contains('actualCost'));
      expect(validate(cost: '-5'), contains('actualCost'));
      expect(validate(cost: '1,500'), contains('actualCost'));
      expect(validate(cost: '1500.505'), contains('actualCost'));
      expect(validate(cost: '10000000.01'), contains('actualCost'));
      expect(validate(cost: '0'), isEmpty);
      expect(validate(cost: '1500.50'), isEmpty);
      expect(validate(cost: '10000000'), isEmpty);
    });
  });

  group('formatting', () {
    test('money is formatted, never computed', () {
      expect(formatMoney(45000), 'Rs 45,000');
      expect(formatMoney(1250.5), 'Rs 1,250.50');
      expect(formatMoney(null), '—');
    });
  });

  group('WorkOrdersApi', () {
    test('the list sends status by NAME and no technician id', () async {
      late Uri requested;
      final api = WorkOrdersApi(_client((request) async {
        requested = request.url;
        return _json(_page([_row(1)]));
      }));

      final result = await api.list(status: 'InProgress');

      expect(requested.path, '/api/workorders');
      expect(requested.queryParameters, {'status': 'InProgress', 'page': '1', 'pageSize': '20'});
      expect(result.items.single.assetTag, 'PRJ-MAB101-01');
    });

    test('complete sends the cost as a number, the outcome by NAME and no photo URL', () async {
      late http.Request sent;
      final api = WorkOrdersApi(_client((request) async {
        sent = request;
        return http.Response('', 204);
      }));

      await api.complete(7, outcome: 'TemporaryFix', actualCost: ' 1500.50 ', resolutionNote: '  $_note\n');

      expect(sent.url.path, '/api/workorders/7/complete');
      expect(sent.body, contains('"actualCost":1500.5'));
      expect(jsonDecode(sent.body), {
        'actualCost': 1500.5,
        'outcome': 'TemporaryFix',
        'resolutionNote': _note,
      });
    });

    test('the photo goes to the work order, as one part named "photo"', () async {
      late http.Request sent;
      final api = WorkOrdersApi(_client((request) async {
        sent = request;
        return _json({'completionPhotoUrl': 'https://storage.test/workorders/7/a.jpg'}, 201);
      }));

      final url = await api.uploadCompletionPhoto(7, bytes: _jpeg(), contentType: 'image/jpeg');

      expect(sent.url.path, '/api/workorders/7/photo');
      expect(sent.headers['content-type'], startsWith('multipart/form-data'));
      expect(latin1.decode(sent.bodyBytes), contains('name="photo"'));
      expect(url, 'https://storage.test/workorders/7/a.jpg');
    });
  });

  group('MyJobsScreen', () {
    Future<void> pump(WidgetTester tester, MockClientHandler handler) => _pumpRouted(
          tester,
          handler: handler,
          initialLocation: MyJobsScreen.path,
          routes: [
            GoRoute(path: MyJobsScreen.path, builder: (_, __) => const MyJobsScreen()),
            GoRoute(path: '/jobs/:id', builder: (_, state) => Text('JOB ${state.pathParameters['id']}')),
          ],
        );

    testWidgets('no jobs is an empty state, not an error', (tester) async {
      await pump(tester, (_) async => _json(_page([])));

      expect(find.byType(EmptyView), findsOneWidget);
      expect(find.byType(ErrorView), findsNothing);
      expect(find.textContaining('No jobs are assigned to you'), findsOneWidget);
    });

    testWidgets('a failed request is an ErrorView with a retry', (tester) async {
      var calls = 0;
      await pump(tester, (_) async {
        calls++;
        return calls == 1 ? _problem(500, 'Boom') : _json(_page([_row(1)]));
      });

      expect(find.byType(ErrorView), findsOneWidget);
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(find.text('PRJ-MAB101-01'), findsOneWidget);
    });

    testWidgets('the status filter goes to the API by NAME; a job opens its detail',
        (tester) async {
      final statuses = <String?>[];
      await pump(tester, (request) async {
        statuses.add(request.url.queryParameters['status']);
        return _json(_page([_row(7, status: request.url.queryParameters['status'] ?? 'Scheduled')]));
      });

      await tester.tap(find.widgetWithText(ChoiceChip, 'In Progress'));
      await tester.pumpAndSettle();
      expect(statuses, [null, 'InProgress']);

      await tester.tap(find.text('PRJ-MAB101-01'));
      await tester.pumpAndSettle();
      expect(find.text('JOB 7'), findsOneWidget);
    });

    testWidgets('an empty filter says so and offers the way back', (tester) async {
      await pump(tester, (request) async =>
          _json(_page(request.url.queryParameters['status'] == null ? [_row(1)] : [])));

      await tester.tap(find.widgetWithText(ChoiceChip, 'Completed'));
      await tester.pumpAndSettle();
      expect(find.textContaining('None of your jobs are completed'), findsOneWidget);

      await tester.tap(find.text('Show all jobs'));
      await tester.pumpAndSettle();
      expect(find.text('PRJ-MAB101-01'), findsOneWidget);
    });
  });

  group('JobDetailScreen', () {
    Future<void> pump(WidgetTester tester, Map<String, dynamic> detail) => _pumpRouted(
          tester,
          handler: (_) async => _json(detail),
          initialLocation: JobDetailScreen.location(7),
          routes: [
            GoRoute(path: '/jobs/:id', builder: (_, __) => const JobDetailScreen(workOrderId: 7)),
            GoRoute(path: '/jobs/:id/complete', builder: (_, __) => const Text('COMPLETE FORM')),
            GoRoute(path: '/assets/:id', builder: (_, __) => const Text('ASSET')),
          ],
        );

    testWidgets('shows the asset, room, slot, parts and the diagnosis with its evidence',
        (tester) async {
      await pump(tester, _detail());

      expect(find.text('Ceiling Projector'), findsOneWidget);
      expect(find.text('MAB101 · Lecture Hall A · Floor 1'), findsOneWidget);
      expect(find.text(formatSlot('2026-09-28T03:30:00Z', '2026-09-28T05:00:00Z')), findsOneWidget);
      expect(find.text('Cooling fan 80mm, thermal paste'), findsOneWidget);
      // Verbatim, newline and all.
      expect(find.text('Projector cuts out 20 min into every lecture.\nFan very loud.'), findsOneWidget);
      expect(find.text('Overheating due to a failing cooling fan'), findsOneWidget);
      expect(find.text('• 2026-09-02: fan bearing weak, temporary fix.'), findsOneWidget);
      expect(find.text('Most likely'), findsOneWidget);
      expect(find.textContaining('not a decision'), findsOneWidget);

      await tester.tap(find.text('Complete job'));
      await tester.pumpAndSettle();
      expect(find.text('COMPLETE FORM'), findsOneWidget);
    });

    testWidgets('no diagnosis, no slot and no parts each say so', (tester) async {
      await pump(tester, _detail(diagnosis: null, slots: const [], parts: null));

      expect(find.text('The diagnostic agent has not looked at this fault.'), findsOneWidget);
      expect(find.textContaining('No visit booked yet'), findsOneWidget);
      expect(find.text('None listed.'), findsOneWidget);
    });

    testWidgets('a failed diagnosis is told apart from none, with its reason', (tester) async {
      await pump(tester, _detail(diagnosis: {
        ..._readableDiagnosis,
        'validationResult': 'SafeFailure',
        'errorMessage': 'model timed out',
        'outputReadable': false,
        'hypotheses': <Object>[],
      }));

      expect(find.textContaining('could not produce a diagnosis'), findsOneWidget);
      expect(find.textContaining('model timed out'), findsOneWidget);
    });

    testWidgets('no Complete button on a job that is not mine or not live', (tester) async {
      await pump(tester, _detail(technicianId: 99));
      expect(find.text('Complete job'), findsNothing);
    });

    testWidgets('a completed job offers nothing to complete', (tester) async {
      await pump(tester, _detail(status: 'Completed'));
      expect(find.text('Complete job'), findsNothing);
    });
  });

  group('CompleteJobScreen', () {
    Future<void> pump(
      WidgetTester tester, {
      required MockClientHandler handler,
      Future<XFile?> Function()? pick,
    }) =>
        _pumpRouted(
          tester,
          handler: handler,
          initialLocation: CompleteJobScreen.location(7),
          picker: _FakePicker(pick ?? () async => XFile.fromData(_jpeg(), mimeType: 'image/jpeg')),
          routes: [
            GoRoute(path: '/jobs/:id/complete', builder: (_, __) => const CompleteJobScreen(workOrderId: 7)),
            GoRoute(path: '/jobs/:id', builder: (_, __) => const Text('JOB DETAIL')),
          ],
        );

    Future<void> fill(WidgetTester tester, {String note = _note, bool photo = false}) async {
      await tester.tap(find.byType(DropdownButtonFormField<String>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Temporary Fix').last);
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextField, 'Actual cost (Rs)'), '1500');
      await tester.enterText(find.widgetWithText(TextField, 'Resolution note'), note);
      if (photo) {
        await tester.tap(find.text('Add completion photo'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('Choose from gallery'));
        await tester.pumpAndSettle();
      }
    }

    testWidgets('"done" is refused by validate() and nothing is sent', (tester) async {
      final calls = <String>[];
      await pump(tester, handler: (request) async {
        calls.add('${request.method} ${request.url.path}');
        return _json(_detail());
      });

      await fill(tester, note: 'done');
      await tester.tap(find.widgetWithText(FilledButton, 'Complete job'));
      await tester.pumpAndSettle();

      expect(find.textContaining('at least $minResolutionNoteLength characters.'), findsWidgets);
      expect(calls, ['GET /api/workorders/7']);
    });

    testWidgets('the photo is uploaded FIRST, then the job is completed', (tester) async {
      final calls = <String>[];
      await pump(tester, handler: (request) async {
        calls.add('${request.method} ${request.url.path}');
        return switch (request.url.path) {
          '/api/workorders/7/photo' => _json({'completionPhotoUrl': 'https://s.test/a.jpg'}, 201),
          '/api/workorders/7/complete' => http.Response('', 204),
          _ => _json(_detail()),
        };
      });

      await fill(tester, photo: true);
      await tester.tap(find.widgetWithText(FilledButton, 'Complete job'));
      await tester.pumpAndSettle();

      expect(calls.where((c) => c.startsWith('POST')), [
        'POST /api/workorders/7/photo',
        'POST /api/workorders/7/complete',
      ]);
      expect(find.text('JOB DETAIL'), findsOneWidget);
    });

    testWidgets('a failed upload says the job is NOT completed, and can complete without it',
        (tester) async {
      final posts = <String>[];
      await pump(tester, handler: (request) async {
        if (request.method == 'POST') posts.add(request.url.path);
        return switch (request.url.path) {
          '/api/workorders/7/photo' => _problem(503, 'Photo storage is unavailable.'),
          '/api/workorders/7/complete' => http.Response('', 204),
          _ => _json(_detail()),
        };
      });

      await fill(tester, photo: true);
      await tester.tap(find.widgetWithText(FilledButton, 'Complete job'));
      await tester.pumpAndSettle();

      expect(find.textContaining('has not been completed yet'), findsOneWidget);
      expect(posts, ['/api/workorders/7/photo']);

      await tester.tap(find.text('Complete without photo'));
      await tester.pumpAndSettle();

      expect(posts, ['/api/workorders/7/photo', '/api/workorders/7/complete']);
      expect(find.text('JOB DETAIL'), findsOneWidget);
    });

    testWidgets("the API's refusal is shown on the form, and a retry does not re-upload",
        (tester) async {
      final posts = <String>[];
      await pump(tester, handler: (request) async {
        if (request.method == 'POST') posts.add(request.url.path);
        return switch (request.url.path) {
          '/api/workorders/7/photo' => _json({'completionPhotoUrl': 'https://s.test/a.jpg'}, 201),
          '/api/workorders/7/complete' => posts.where((p) => p.endsWith('complete')).length == 1
              ? _problem(409, 'Work order 7 cannot do that from where it is now.')
              : http.Response('', 204),
          _ => _json(_detail()),
        };
      });

      await fill(tester, photo: true);
      await tester.tap(find.widgetWithText(FilledButton, 'Complete job'));
      await tester.pumpAndSettle();
      expect(find.text('Work order 7 cannot do that from where it is now.'), findsOneWidget);

      await tester.tap(find.widgetWithText(FilledButton, 'Complete job'));
      await tester.pumpAndSettle();

      expect(posts, [
        '/api/workorders/7/photo',
        '/api/workorders/7/complete',
        '/api/workorders/7/complete',
      ]);
    });

    testWidgets('a job that is already completed has nothing to complete', (tester) async {
      await pump(tester, handler: (_) async => _json(_detail(status: 'Completed')));

      expect(find.byType(EmptyView), findsOneWidget);
      expect(find.textContaining('nothing to complete'), findsOneWidget);
    });
  });
}
