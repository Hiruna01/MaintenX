import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
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
import 'package:maintenx_mobile/features/reports/clarification.dart';
import 'package:maintenx_mobile/features/reports/clarification_screen.dart';
import 'package:maintenx_mobile/features/reports/my_reports_screen.dart';
import 'package:maintenx_mobile/features/reports/report_photo.dart';
import 'package:maintenx_mobile/features/reports/reports_api.dart';
import 'package:maintenx_mobile/features/reports/submit_report_screen.dart';
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

http.Response _problem(int status, String detail) =>
    _json({'status': status, 'title': 'Problem', 'detail': detail}, status);

/// A JPEG's signature followed by filler — big enough to go out in several chunks.
Uint8List _jpeg([int length = 200 * 1024]) =>
    Uint8List(length)..setAll(0, const [0xFF, 0xD8, 0xFF]);

Map<String, dynamic> _question(
  int id,
  String answerType, {
  String text = 'A question?',
  List<String>? options,
  String? answerText,
}) =>
    {
      'id': id,
      'reportId': 5,
      'workflowId': 9,
      'questionText': text,
      'answerType': answerType,
      'options': options,
      'displayOrder': id,
      'answerText': answerText,
      'answeredAt': answerText == null ? null : '2026-09-23T08:00:00Z',
      'createdAt': '2026-09-23T07:00:00Z',
      'updatedAt': '2026-09-23T07:00:00Z',
    };

/// One of each bounded control, the shape the clarifier actually produces.
List<Map<String, dynamic>> _form() => [
      _question(1, 'YesNo', text: 'Is it safe to leave switched on?'),
      _question(2, 'SingleSelect',
          text: 'How often does it cut out?',
          options: ['Every few minutes', 'Once a lecture', 'Only once so far']),
      _question(3, 'ShortText', text: 'What does the screen show when it fails?'),
    ];

Map<String, dynamic> _row(
  int id, {
  String status = 'Submitted',
  int unanswered = 0,
  String description = 'Projector cuts out mid lecture',
}) =>
    {
      'id': id,
      'reporterId': 3,
      'roomId': 1,
      'roomName': 'Lecture Hall A',
      'assetId': null,
      'description': description,
      'status': status,
      'unansweredQuestionCount': unanswered,
      'createdAt': '2026-09-23T07:00:00Z',
      'updatedAt': '2026-09-23T07:00:00Z',
    };

Map<String, dynamic> _page(List<Object> items, {int page = 1, int totalPages = 1}) => {
      'items': items,
      'page': page,
      'pageSize': 20,
      'totalCount': items.length,
      'totalPages': totalPages,
    };

/// The screen under test at [initialLocation], with every place it can navigate to a
/// labelled stub — `context.go` needs a real router.
Future<void> _pumpRouted(
  WidgetTester tester, {
  required ApiClient client,
  required String initialLocation,
  required List<RouteBase> routes,
  ImagePicker? picker,
}) async {
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
        apiClientProvider.overrideWithValue(client),
        if (picker != null) imagePickerProvider.overrideWithValue(picker),
      ],
      child: MaterialApp.router(routerConfig: router),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('ReportsApi', () {
    test('the list sends filters by NAME and leaves empty ones out', () async {
      late Uri requested;
      final api = ReportsApi(_client((request) async {
        requested = request.url;
        return _json(_page([_row(1)]));
      }));

      final result = await api.list(search: '  ', status: 'AwaitingClarification', page: 2);

      expect(requested.path, '/api/reports');
      expect(requested.queryParameters, {
        'status': 'AwaitingClarification',
        'page': '2',
        'pageSize': '20',
      });
      expect(result.items.single.roomName, 'Lecture Hall A');
    });

    test('answers go in ONE request carrying the whole form', () async {
      final requests = <http.Request>[];
      final api = ReportsApi(_client((request) async {
        requests.add(request);
        return http.Response('', 204);
      }));

      await api.submitAnswers(5, {1: 'Yes', 2: 'Once a lecture', 3: 'blue screen'});

      expect(requests, hasLength(1));
      expect(requests.single.url.path, '/api/reports/5/clarifications');
      expect(jsonDecode(requests.single.body), {
        'answers': [
          {'questionId': 1, 'answerText': 'Yes'},
          {'questionId': 2, 'answerText': 'Once a lecture'},
          {'questionId': 3, 'answerText': 'blue screen'},
        ],
      });
    });

    test('a photo is one multipart part named "photo", and progress moves to the total',
        () async {
      late http.Request sent;
      final api = ReportsApi(_client((request) async {
        sent = request;
        return _json({'photoUrl': 'https://storage.test/reports/abc.jpg'}, 201);
      }));
      final bytes = _jpeg();
      final progress = <int>[];

      final url = await api.uploadPhoto(
        5,
        bytes: bytes,
        contentType: 'image/jpeg',
        onProgress: (done, total) {
          expect(total, bytes.length);
          progress.add(done);
        },
      );

      expect(url, 'https://storage.test/reports/abc.jpg');
      expect(sent.url.path, '/api/reports/5/photo');
      expect(sent.headers['content-type'], startsWith('multipart/form-data; boundary='));
      final body = latin1.decode(sent.bodyBytes);
      expect(body, contains('name="photo"'));
      expect(body, contains('content-type: image/jpeg'));
      // In steps, not one jump from nothing to everything.
      expect(progress.first, 0);
      expect(progress.last, bytes.length);
      expect(progress.length, greaterThan(3));
    });

    test('a 503 from storage is an ApiException carrying the API\'s own message', () async {
      final api = ReportsApi(_client((_) async => _problem(503, 'Storage is down.')));

      expect(
        () => api.uploadPhoto(5, bytes: _jpeg(10), contentType: 'image/jpeg'),
        throwsA(isA<ApiException>()
            .having((e) => e.statusCode, 'statusCode', 503)
            .having((e) => e.message, 'message', 'Storage is down.')),
      );
    });
  });

  group('validateClarificationAnswers', () {
    final questions = _form().map(ClarificationQuestion.fromJson).toList();

    test('every question needs an answer — the API refuses a partial form', () {
      final errors = validateClarificationAnswers(questions, {3: '   '});

      expect(errors.keys, [1, 2, 3]);
      expect(errors[1], 'Choose yes or no.');
    });

    test('a single-select answer must be one of the options the API supplied', () {
      final errors = validateClarificationAnswers(
        questions,
        {1: 'Yes', 2: 'Sometimes', 3: 'blue screen'},
      );

      expect(errors, {2: 'Choose one of the options.'});
    });

    test('100 characters is the cap, counted the way the API counts them', () {
      expect(validateClarificationAnswers(questions,
          {1: 'No', 2: 'Once a lecture', 3: 'x' * 100}), isEmpty);
      // 50 emoji look like 50 characters and are 100 UTF-16 code units; 51 are over.
      expect(
        validateClarificationAnswers(questions, {1: 'No', 2: 'Once a lecture', 3: '🔥' * 51}),
        {3: 'Keep it to 100 characters.'},
      );
    });

    test('an unknown answer type is not renderable — there is no text-box fallback', () {
      expect(ClarificationQuestion.fromJson(_question(1, 'FreeText')).isRenderable, isFalse);
      expect(ClarificationQuestion.fromJson(_question(1, 'SingleSelect', options: []))
          .isRenderable, isFalse);
    });
  });

  group('ClarificationScreen', () {
    Future<List<http.Request>> pump(
      WidgetTester tester,
      List<Map<String, dynamic>> questions, {
      http.Response Function()? onSubmit,
    }) async {
      tester.view.physicalSize = const Size(800, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final posts = <http.Request>[];
      await _pumpRouted(
        tester,
        initialLocation: ClarificationScreen.location(5),
        client: _client((request) async {
          if (request.method == 'POST') {
            posts.add(request);
            return onSubmit?.call() ?? http.Response('', 204);
          }
          return _json(questions);
        }),
        routes: [
          GoRoute(
            path: MyReportsScreen.path,
            builder: (_, __) => const Text('REPORT LIST'),
            routes: [
              GoRoute(
                path: ClarificationScreen.subPath,
                builder: (_, state) => ClarificationScreen(
                  reportId: int.tryParse(state.pathParameters['id']!),
                ),
              ),
            ],
          ),
        ],
      );
      return posts;
    }

    testWidgets('each question is ONE bounded control — and none of it is a chat',
        (tester) async {
      await pump(tester, _form());

      expect(find.byType(SegmentedButton<String>), findsOneWidget);
      expect(find.byType(DropdownButtonFormField<String>), findsOneWidget);
      // The short-text answer is the only text box on the form, and it is capped.
      expect(find.byType(TextField), findsOneWidget);
      expect(find.text('0/100'), findsOneWidget);
      // Submitted as a form, not sent as a message.
      expect(find.text('Submit answers'), findsOneWidget);
      expect(find.textContaining('Send'), findsNothing);
      expect(find.byIcon(Icons.send), findsNothing);
    });

    testWidgets('the dropdown offers exactly the options the API supplied', (tester) async {
      await pump(tester, _form());

      await tester.tap(find.byType(DropdownButtonFormField<String>));
      await tester.pumpAndSettle();

      final offered = tester
          .widgetList<DropdownMenuItem<String>>(find.byType(DropdownMenuItem<String>))
          .map((item) => item.value)
          .toSet();
      expect(offered, {'Every few minutes', 'Once a lecture', 'Only once so far'});
    });

    testWidgets('an incomplete form is refused by validate() and nothing is sent',
        (tester) async {
      final posts = await pump(tester, _form());

      await tester.tap(find.text('Submit answers'));
      await tester.pumpAndSettle();

      expect(find.text('Choose yes or no.'), findsOneWidget);
      expect(find.text('Choose one of the options.'), findsOneWidget);
      expect(find.text('Answer this question.'), findsOneWidget);
      expect(posts, isEmpty);
    });

    testWidgets('a complete form goes in one POST and the exchange is over', (tester) async {
      final posts = await pump(tester, _form());

      await tester.tap(find.text('Yes'));
      await tester.tap(find.byType(DropdownButtonFormField<String>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Once a lecture').last);
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), '  blue "no signal" screen  ');
      await tester.tap(find.text('Submit answers'));
      await tester.pumpAndSettle();

      expect(posts, hasLength(1));
      expect(jsonDecode(posts.single.body), {
        'answers': [
          {'questionId': 1, 'answerText': 'Yes'},
          {'questionId': 2, 'answerText': 'Once a lecture'},
          {'questionId': 3, 'answerText': 'blue "no signal" screen'},
        ],
      });
      // Back to the list — no reply, no follow-up round.
      expect(find.text('REPORT LIST'), findsOneWidget);
    });

    testWidgets("the API's refusal is shown on the form", (tester) async {
      await pump(
        tester,
        [_question(1, 'YesNo')],
        onSubmit: () => _problem(409, 'Report 5 is not waiting on an answer.'),
      );

      await tester.tap(find.text('No'));
      await tester.tap(find.text('Submit answers'));
      await tester.pumpAndSettle();

      expect(find.text('Report 5 is not waiting on an answer.'), findsOneWidget);
    });

    testWidgets('an unknown answer type refuses the form instead of offering a text box',
        (tester) async {
      await pump(tester, [_question(1, 'YesNo'), _question(2, 'FreeText')]);

      expect(find.text('This form cannot be shown'), findsOneWidget);
      expect(find.byType(TextField), findsNothing);
      expect(find.text('Submit answers'), findsNothing);
    });

    testWidgets('an answered form says it is done and does not replay it', (tester) async {
      await pump(tester, [_question(1, 'YesNo', answerText: 'Yes')]);

      expect(find.text('You have already answered these questions. Thank you.'),
          findsOneWidget);
      expect(find.text('A question?'), findsNothing);
      expect(find.text('Submit answers'), findsNothing);
    });

    testWidgets('no questions is an empty state, not an error', (tester) async {
      await pump(tester, const []);

      expect(find.byType(EmptyView), findsOneWidget);
      expect(find.byType(ErrorView), findsNothing);
    });
  });

  group('MyReportsScreen', () {
    Future<List<Uri>> pump(WidgetTester tester, MockClientHandler respond) async {
      final requests = <Uri>[];
      await _pumpRouted(
        tester,
        initialLocation: MyReportsScreen.path,
        client: _client((request) {
          requests.add(request.url);
          return respond(request);
        }),
        routes: [
          GoRoute(
            path: MyReportsScreen.path,
            builder: (_, __) => const MyReportsScreen(),
            routes: [
              GoRoute(
                path: ClarificationScreen.subPath,
                builder: (_, state) => Text('CLARIFY ${state.pathParameters['id']}'),
              ),
            ],
          ),
          GoRoute(path: SubmitReportScreen.path, builder: (_, __) => const Text('SUBMIT')),
        ],
      );
      return requests;
    }

    testWidgets('no reports at all is an empty state, not an error', (tester) async {
      await pump(tester, (_) async => _json(_page(const [])));

      expect(find.byType(EmptyView), findsOneWidget);
      expect(find.byType(ErrorView), findsNothing);
      expect(find.textContaining('You have not reported anything yet.'), findsOneWidget);
    });

    testWidgets('a failed request is an ErrorView with a retry', (tester) async {
      var calls = 0;
      await pump(tester, (_) async {
        calls++;
        return calls == 1 ? http.Response('', 500) : _json(_page([_row(1)]));
      });

      expect(find.byType(ErrorView), findsOneWidget);
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(find.text('Projector cuts out mid lecture'), findsOneWidget);
    });

    testWidgets('a report waiting on the reporter says so and opens its form',
        (tester) async {
      await pump(
        tester,
        (_) async => _json(_page([
              _row(7, status: 'AwaitingClarification', unanswered: 2),
              _row(8, status: 'WorkOrderRaised', description: 'Aircon dripping'),
            ])),
      );

      expect(find.text('Awaiting Clarification'), findsWidgets);
      expect(find.text('Work Order Raised'), findsWidgets);
      expect(find.text('2 questions waiting on you'), findsOneWidget);

      await tester.tap(find.text('2 questions waiting on you'));
      await tester.pumpAndSettle();
      expect(find.text('CLARIFY 7'), findsOneWidget);
    });

    testWidgets('the status filter goes to the API by NAME; nothing matching is empty',
        (tester) async {
      final requests = await pump(
        tester,
        (request) async => _json(_page(
            request.url.queryParameters.containsKey('status') ? const [] : [_row(1)])),
      );

      await tester.tap(find.widgetWithText(ChoiceChip, 'Clarified'));
      await tester.pumpAndSettle();

      expect(requests.last.queryParameters['status'], 'Clarified');
      expect(find.text('No reports match your search or filter.'), findsOneWidget);
      expect(find.byType(ErrorView), findsNothing);

      await tester.tap(find.text('Clear filters'));
      await tester.pumpAndSettle();
      expect(requests.last.queryParameters.containsKey('status'), isFalse);
    });

    testWidgets('the search is debounced — one request for a word, not one per letter',
        (tester) async {
      final requests = await pump(tester, (_) async => _json(_page([_row(1)])));
      final before = requests.length;

      await tester.enterText(find.byType(TextField), 'proj');
      await tester.pump(const Duration(milliseconds: 100));
      await tester.enterText(find.byType(TextField), 'projector');
      await tester.pump(const Duration(milliseconds: 100));
      expect(requests.length, before);

      await tester.pump(const Duration(milliseconds: 400));
      await tester.pumpAndSettle();
      expect(requests.length, before + 1);
      expect(requests.last.queryParameters['search'], 'projector');
    });
  });

  group('SubmitReportScreen photo', () {
    const room = {'id': 1, 'buildingId': 1, 'name': 'Lecture Hall A', 'code': 'MAB-101', 'floor': 1};

    Future<void> pump(
      WidgetTester tester, {
      required MockClientHandler handler,
      required Future<XFile?> Function() pick,
    }) async {
      tester.view.physicalSize = const Size(800, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      await _pumpRouted(
        tester,
        initialLocation: SubmitReportScreen.path,
        client: _client(handler),
        picker: _FakePicker(pick),
        routes: [
          GoRoute(path: SubmitReportScreen.path, builder: (_, __) => const SubmitReportScreen()),
          GoRoute(path: MyReportsScreen.path, builder: (_, __) => const Text('REPORT LIST')),
        ],
      );
    }

    Future<void> fillAndAttach(WidgetTester tester) async {
      await tester.enterText(find.byType(TextField), 'Projector cuts out mid lecture');
      await tester.tap(find.byType(DropdownButtonFormField<int>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('MAB-101 — Lecture Hall A').last);
      await tester.pumpAndSettle();

      await tester.tap(find.text('Add photo'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Choose from gallery'));
      await tester.pumpAndSettle();
    }

    Future<XFile?> jpeg() async => XFile.fromData(_jpeg(), mimeType: 'image/jpeg');

    testWidgets('the report is filed first, then the photo is uploaded to it', (tester) async {
      final calls = <String>[];
      await pump(
        tester,
        pick: jpeg,
        handler: (request) async {
          calls.add('${request.method} ${request.url.path}');
          if (request.url.path == '/api/rooms') return _json([room]);
          if (request.url.path == '/api/reports') return _json({'id': 42}, 201);
          return _json({'photoUrl': 'https://storage.test/x.jpg'}, 201);
        },
      );

      await fillAndAttach(tester);
      expect(find.text('Change photo'), findsOneWidget);
      expect(find.textContaining('JPEG'), findsOneWidget);

      await tester.tap(find.text('Submit report'));
      await tester.pumpAndSettle();

      expect(calls, ['GET /api/rooms', 'POST /api/reports', 'POST /api/reports/42/photo']);
      expect(find.text('REPORT LIST'), findsOneWidget);
    });

    testWidgets('upload progress is shown, then "saving" while the API stores it',
        (tester) async {
      final stored = Completer<http.Response>();
      await pump(
        tester,
        pick: jpeg,
        handler: (request) async {
          if (request.url.path == '/api/rooms') return _json([room]);
          if (request.url.path == '/api/reports') return _json({'id': 42}, 201);
          return stored.future;
        },
      );

      await fillAndAttach(tester);
      await tester.tap(find.text('Submit report'));
      await tester.pump();
      await tester.pump();

      // Every byte is out and the API has not answered yet.
      expect(find.byType(LinearProgressIndicator), findsOneWidget);
      expect(find.text('Saving photo…'), findsOneWidget);

      stored.complete(_json({'photoUrl': 'https://storage.test/x.jpg'}, 201));
      await tester.pumpAndSettle();
      expect(find.text('REPORT LIST'), findsOneWidget);
    });

    testWidgets('a failed upload says the report IS filed, and retry does not file it again',
        (tester) async {
      var reportPosts = 0;
      var photoPosts = 0;
      await pump(
        tester,
        pick: jpeg,
        handler: (request) async {
          if (request.url.path == '/api/rooms') return _json([room]);
          if (request.url.path == '/api/reports') {
            reportPosts++;
            return _json({'id': 42}, 201);
          }
          photoPosts++;
          return photoPosts == 1
              ? _problem(503, 'Photo storage is unavailable.')
              : _json({'photoUrl': 'https://storage.test/x.jpg'}, 201);
        },
      );

      await fillAndAttach(tester);
      await tester.tap(find.text('Submit report'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Your report has been filed'), findsOneWidget);
      expect(find.textContaining('Photo storage is unavailable.'), findsOneWidget);
      expect(find.text('Continue without photo'), findsOneWidget);

      await tester.tap(find.text('Retry upload'));
      await tester.pumpAndSettle();

      expect(reportPosts, 1);
      expect(photoPosts, 2);
      expect(find.text('REPORT LIST'), findsOneWidget);
    });

    testWidgets('a photo the API would refuse is refused when picked', (tester) async {
      await pump(
        tester,
        pick: () async => XFile.fromData(Uint8List(10), mimeType: 'image/gif'),
        handler: (request) async => _json([room]),
      );

      await fillAndAttach(tester);

      expect(find.text('Only JPEG or PNG photos can be attached.'), findsOneWidget);
      expect(find.text('Add photo'), findsOneWidget);
    });

    testWidgets('camera permission denied is explained, not a crash', (tester) async {
      await pump(
        tester,
        pick: () async => throw PlatformException(code: 'camera_access_denied'),
        handler: (request) async => _json([room]),
      );

      await tester.tap(find.text('Add photo'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Take a photo'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Camera access is off.'), findsOneWidget);
    });
  });
}
