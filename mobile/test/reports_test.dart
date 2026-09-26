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
import 'package:maintenx_mobile/features/assets/asset.dart';
import 'package:maintenx_mobile/features/assets/scan_asset_screen.dart';
import 'package:maintenx_mobile/features/reports/clarification.dart';
import 'package:maintenx_mobile/features/reports/clarification_screen.dart';
import 'package:maintenx_mobile/features/reports/clarifier_wait.dart';
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
  String? stage = 'BeingReviewed',
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
      if (stage != null) 'stage': stage,
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

/// A `ReportDetailDto` as the wait polls it: the questions written so far and the agent
/// steps recorded so far.
Map<String, dynamic> _detail({
  List<Map<String, dynamic>> questions = const [],
  List<Map<String, dynamic>> steps = const [],
}) =>
    {
      'id': 42,
      'status': questions.isEmpty ? 'Submitted' : 'AwaitingClarification',
      'description': 'Projector cuts out mid lecture',
      'clarificationQuestions': questions,
      'agentSteps': steps,
    };

/// The clarifier's agent-run step, in the shape WorkflowRunner records it.
Map<String, dynamic> _clarifierRun({
  int asked = 0,
  String validationResult = 'Ok',
  String toolCallsJson = '[]',
}) =>
    {
      'id': 1,
      'workflowId': 9,
      'agentName': 'clarifier',
      'toolCallsJson': toolCallsJson,
      'validationResult': validationResult,
      'payloadJson': jsonEncode({
        'questions': [
          for (var i = 0; i < asked; i++)
            {'question_text': 'Q$i?', 'answer_type': 'yes_no', 'options': null},
        ],
      }),
    };

/// The planner's agent-run step: its plan, delegating to [agents] in order.
Map<String, dynamic> _plannerRun(List<String> agents, {String validationResult = 'Ok'}) => {
      'id': 2,
      'workflowId': 9,
      'agentName': 'planner',
      'toolCallsJson': '[]',
      'validationResult': validationResult,
      'payloadJson': jsonEncode({
        'steps': [
          for (final agent in agents) {'agent': agent, 'purpose': 'For this report.'},
        ],
        'rationale': 'Spy plan.',
      }),
    };

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

  group('readClarifierProgress', () {
    test('nothing recorded yet is still running', () {
      expect(readClarifierProgress(_detail()), ClarifierProgress.running);
    });

    test('questions on the report are asked', () {
      expect(readClarifierProgress(_detail(questions: _form())), ClarifierProgress.asked);
    });

    test('a run that asked, whose rows are not written yet, is still running', () {
      // The runner saves the step before the question rows. Reading this as "nothing to
      // ask" would send the reporter away from a form about to exist.
      expect(
        readClarifierProgress(_detail(steps: [_clarifierRun(asked: 2)])),
        ClarifierProgress.running,
      );
    });

    test('a run that asked nothing is nothing to ask', () {
      expect(
        readClarifierProgress(_detail(steps: [_clarifierRun()])),
        ClarifierProgress.nothingToAsk,
      );
    });

    test('a failed run is failed, whichever way it failed', () {
      for (final result in ['SafeFailure', 'CallFailed']) {
        expect(
          readClarifierProgress(_detail(steps: [_clarifierRun(validationResult: result)])),
          ClarifierProgress.failed,
        );
      }
    });

    test("the clarifier's tool calls are not its answer", () {
      final toolCall = _clarifierRun(toolCallsJson: '[{"tool":"get_room"}]');
      expect(readClarifierProgress(_detail(steps: [toolCall])), ClarifierProgress.running);
    });

    test('a plan that leaves the clarifier out is nothing to ask — no clarifier step will come', () {
      expect(
        readClarifierProgress(_detail(steps: [_plannerRun(['diagnostic', 'strategist'])])),
        ClarifierProgress.nothingToAsk,
      );
    });

    test('a plan that includes the clarifier keeps the wait going until it has run', () {
      expect(
        readClarifierProgress(
            _detail(steps: [_plannerRun(['clarifier', 'diagnostic', 'strategist'])])),
        ClarifierProgress.running,
      );
    });

    test('a planner that failed or was rejected means the default plan, so the wait goes on', () {
      for (final result in ['SafeFailure', 'Rejected']) {
        expect(
          readClarifierProgress(
              _detail(steps: [_plannerRun(['diagnostic', 'strategist'], validationResult: result)])),
          ClarifierProgress.running,
        );
      }
    });
  });

  group('ClarificationScreen, straight after filing', () {
    Future<List<String>> pump(
      WidgetTester tester,
      Future<http.Response> Function(int poll) onPoll,
    ) async {
      tester.view.physicalSize = const Size(800, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final calls = <String>[];
      var polls = 0;
      final router = GoRouter(
        initialLocation: ClarificationScreen.location(42, waitForQuestions: true),
        routes: [
          GoRoute(
            path: MyReportsScreen.path,
            builder: (_, __) => const Text('REPORT LIST'),
            routes: [
              GoRoute(
                path: ClarificationScreen.subPath,
                builder: (_, state) => ClarificationScreen(
                  reportId: int.tryParse(state.pathParameters['id']!),
                  waitForQuestions:
                      state.uri.queryParameters[ClarificationScreen.waitingParam] == '1',
                ),
              ),
            ],
          ),
        ],
      );
      addTearDown(router.dispose);

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            apiClientProvider.overrideWithValue(_client((request) async {
              calls.add('${request.method} ${request.url.path}');
              return onPoll(++polls);
            })),
          ],
          child: MaterialApp.router(routerConfig: router),
        ),
      );
      // The spinner never settles, so time is moved on by hand rather than pumpAndSettle.
      await tester.pump();
      return calls;
    }

    testWidgets('it waits for the clarifier, then the form opens in place', (tester) async {
      final calls = await pump(
        tester,
        (poll) async => _json(poll < 3 ? _detail() : _detail(questions: _form())),
      );

      expect(find.text('Report filed'), findsOneWidget);
      expect(find.text('Checking your report…'), findsOneWidget);
      expect(find.byType(SegmentedButton<String>), findsNothing);

      await tester.pump(ClarifierWait.pollEvery);
      await tester.pump(ClarifierWait.pollEvery);
      await tester.pump(const Duration(milliseconds: 300));

      // The form, without going anywhere near the list.
      expect(find.text('A few quick questions'), findsOneWidget);
      expect(find.byType(SegmentedButton<String>), findsOneWidget);
      expect(find.text('REPORT LIST'), findsNothing);
      expect(calls, List.filled(3, 'GET /api/reports/42'));

      // And it stops asking once it has them.
      await tester.pump(ClarifierWait.pollEvery * 3);
      expect(calls, hasLength(3));
    });

    testWidgets('a clarifier that needed nothing says so and stops', (tester) async {
      final calls = await pump(
        tester,
        (poll) async => _json(poll < 2 ? _detail() : _detail(steps: [_clarifierRun()])),
      );

      await tester.pump(ClarifierWait.pollEvery);
      await tester.pump();

      expect(find.textContaining('No questions needed'), findsOneWidget);
      await tester.pump(ClarifierWait.pollEvery * 3);
      expect(calls, hasLength(2));

      await tester.tap(find.text('Go to my reports'));
      await tester.pumpAndSettle();
      expect(find.text('REPORT LIST'), findsOneWidget);
    });

    testWidgets('the reporter can leave while it is still checking', (tester) async {
      await pump(tester, (_) async => _json(_detail()));

      await tester.tap(find.text('Go to my reports'));
      await tester.pumpAndSettle();
      expect(find.text('REPORT LIST'), findsOneWidget);
    });

    testWidgets('one dropped poll is ignored; repeated failures say the report IS filed',
        (tester) async {
      final calls = await pump(tester, (_) async => _problem(500, 'Server error.'));

      await tester.pump(ClarifierWait.pollEvery);
      expect(find.byType(ErrorView), findsNothing);

      await tester.pump(ClarifierWait.pollEvery);
      await tester.pump();
      expect(calls, hasLength(ClarifierWait.maxConsecutiveErrors));
      expect(find.byType(ErrorView), findsOneWidget);
      expect(find.textContaining('Your report has been filed.'), findsOneWidget);
    });

    testWidgets('it gives up after its time limit and points at My reports', (tester) async {
      await pump(tester, (_) async => _json(_detail()));

      await tester.pump(ClarifierWait.slowAfter);
      expect(find.textContaining('taking longer than usual'), findsOneWidget);

      for (var i = 0; i < 100; i++) {
        await tester.pump(ClarifierWait.pollEvery);
      }
      expect(find.textContaining('the questions will be waiting in My reports'), findsOneWidget);
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

    testWidgets('each report shows the stage the API sent, worded for the reporter',
        (tester) async {
      await pump(
        tester,
        (_) async => _json(_page([
              _row(1, status: 'WorkOrderRaised', stage: 'AwaitingApproval'),
              _row(2, status: 'Closed', stage: 'NotGoingAhead', description: 'Door lock jams'),
            ])),
      );

      // By NAME, with its sentence — and never a cost, an estimate or who is sent.
      expect(find.text('Awaiting Approval'), findsOneWidget);
      expect(find.text('A repair has been proposed and is waiting for a manager to sign it off.'),
          findsOneWidget);
      expect(find.text('Not Going Ahead'), findsOneWidget);
      expect(find.text('A manager decided not to go ahead with this repair.'), findsOneWidget);
      expect(find.textContaining('Rs'), findsNothing);
    });

    testWidgets('an unknown stage is shown by its name; no stage means no progress line',
        (tester) async {
      await pump(
        tester,
        (_) async => _json(_page([
              _row(3, status: 'Diagnosed', stage: 'SomethingNew', description: 'Bin overflowing'),
              _row(4, stage: null, description: 'Blind stuck half way'),
            ])),
      );

      // Not guessed at: a member added to the C# enum first still reads as what it is.
      expect(find.text('Something New'), findsOneWidget);
      expect(find.text('Blind stuck half way'), findsOneWidget);
      expect(find.text('Progress'), findsOneWidget);
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

  group('SubmitReportScreen scan', () {
    const hallA = {'id': 1, 'buildingId': 1, 'name': 'Lecture Hall A', 'code': 'MAB-101', 'floor': 1};
    const seminar = {'id': 2, 'buildingId': 1, 'name': 'Seminar Room 1', 'code': 'MAB-201', 'floor': 2};

    // What GET /api/assets/by-tag answers for a sticker in Seminar Room 1.
    final scanned = AssetDetail.fromJson({
      'id': 7,
      'assetTag': 'PRJ-MAB201-01',
      'name': 'Seminar Room 1 Projector',
      'category': {'id': 2, 'name': 'Projector', 'defaultWarrantyMonths': 24},
      'room': seminar,
      'manufacturer': 'Epson',
      'model': 'EB-L630U',
      'installedOn': '2023-02-10',
      'warrantyExpiresOn': null,
      'status': 'Active',
      'serviceHistory': <Object>[],
    });

    Future<List<Map<String, dynamic>>> pump(WidgetTester tester) async {
      tester.view.physicalSize = const Size(800, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final filed = <Map<String, dynamic>>[];
      await _pumpRouted(
        tester,
        initialLocation: SubmitReportScreen.path,
        client: _client((request) async {
          if (request.url.path == '/api/rooms') return _json([hallA, seminar]);
          filed.add(jsonDecode(request.body) as Map<String, dynamic>);
          return _json({'id': 42}, 201);
        }),
        routes: [
          GoRoute(path: SubmitReportScreen.path, builder: (_, __) => const SubmitReportScreen()),
          // The scanner stands in as a screen that "reads" one sticker: a test has no camera,
          // and the real scanner's pick mode is pinned in assets_test.dart.
          GoRoute(
            path: ScanAssetScreen.reportPath,
            builder: (context, _) => TextButton(
              onPressed: () => Navigator.of(context).pop(scanned),
              child: const Text('READ STICKER'),
            ),
          ),
          GoRoute(
            path: '/reports/:id/clarifications',
            builder: (_, __) => const Text('QUESTIONS'),
          ),
        ],
      );
      return filed;
    }

    Future<void> scan(WidgetTester tester) async {
      await tester.tap(find.text("Scan the equipment's sticker"));
      await tester.pumpAndSettle();
      await tester.tap(find.text('READ STICKER'));
      await tester.pumpAndSettle();
    }

    testWidgets('a scanned sticker names the equipment, fills in its room, and is filed with it',
        (tester) async {
      final filed = await pump(tester);

      await scan(tester);
      expect(find.text('Seminar Room 1 Projector'), findsOneWidget);
      expect(find.text('PRJ-MAB201-01'), findsOneWidget);
      // The room came from the asset — no room was chosen by hand.
      expect(find.text('Seminar Room 1'), findsOneWidget);

      await tester.enterText(find.byType(TextField), 'Projector will not power on');
      await tester.tap(find.text('Submit report'));
      await tester.pumpAndSettle();

      expect(filed, [
        {'description': 'Projector will not power on', 'roomId': 2, 'assetId': 7},
      ]);
      expect(find.text('QUESTIONS'), findsOneWidget);
    });

    testWidgets('without a scan no assetId is sent at all', (tester) async {
      final filed = await pump(tester);

      await tester.enterText(find.byType(TextField), 'Projector will not power on');
      await tester.tap(find.text('Choose a room'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Lecture Hall A').last);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Submit report'));
      await tester.pumpAndSettle();

      expect(filed, [
        {'description': 'Projector will not power on', 'roomId': 1},
      ]);
    });

    testWidgets('choosing a different room drops the scanned equipment', (tester) async {
      await pump(tester);
      await scan(tester);

      await tester.tap(find.text('Seminar Room 1'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Lecture Hall A').last);
      await tester.pumpAndSettle();

      // The API would refuse an asset outside the report's room.
      expect(find.text('Seminar Room 1 Projector'), findsNothing);
      expect(find.text("Scan the equipment's sticker"), findsOneWidget);
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
          // Filing opens the report's questions — waiting for them — not the list.
          GoRoute(
            path: '/reports/:id/clarifications',
            builder: (_, state) => Text(
              'QUESTIONS FOR ${state.pathParameters['id']}, '
              'waiting=${state.uri.queryParameters[ClarificationScreen.waitingParam]}',
            ),
          ),
        ],
      );
    }

    Future<void> fillAndAttach(WidgetTester tester) async {
      await tester.enterText(find.byType(TextField), 'Projector cuts out mid lecture');
      // The room is chosen from a bottom sheet listing every room, not a dropdown.
      await tester.tap(find.text('Choose a room'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Lecture Hall A').last);
      await tester.pumpAndSettle();
      expect(find.text('Lecture Hall A'), findsOneWidget);

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
      expect(find.text('QUESTIONS FOR 42, waiting=1'), findsOneWidget);
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
      expect(find.text('QUESTIONS FOR 42, waiting=1'), findsOneWidget);
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
      expect(find.text('QUESTIONS FOR 42, waiting=1'), findsOneWidget);
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
