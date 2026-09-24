import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:maintenx_mobile/core/api_client.dart';
import 'package:maintenx_mobile/core/providers.dart';
import 'package:maintenx_mobile/core/token_storage.dart';
import 'package:maintenx_mobile/features/reports/my_reports_screen.dart';
import 'package:maintenx_mobile/features/verification/confirm_fix_screen.dart';
import 'package:maintenx_mobile/features/verification/pending_confirmations_screen.dart';
import 'package:maintenx_mobile/features/verification/verification.dart';
import 'package:maintenx_mobile/features/verification/verification_api.dart';
import 'package:maintenx_mobile/widgets/empty_view.dart';
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

http.Response _problem(int status, String detail) =>
    _json({'status': status, 'title': 'Problem', 'detail': detail}, status);

Map<String, dynamic> _page(List<Object> items) => {
      'items': items,
      'page': 1,
      'pageSize': 20,
      'totalCount': items.length,
      'totalPages': 1,
    };

Map<String, dynamic> _listItem(int id, {bool isOverdue = false}) => {
      'id': id,
      'workOrderId': 30,
      'reportId': 5,
      'reportDescription': 'Projector cuts out mid lecture',
      'workOrderCompletedAt': '2026-09-15T10:00:00Z',
      'assetId': 2,
      'assetTag': 'PRJ-MAB101-01',
      'dueAt': '2026-09-20T10:00:00Z',
      'status': 'AwaitingReporterResponse',
      'reporterConfirmed': null,
      'isOverdue': isOverdue,
      'reporterRespondedAt': null,
      'createdAt': '2026-09-15T10:00:00Z',
      'updatedAt': '2026-09-20T10:00:00Z',
    };

const _note = 'fan bearing noisy, cleaned + regreased. temp fix, recommend replacement.';

Map<String, dynamic> _detail({
  String status = 'AwaitingReporterResponse',
  bool? confirmed,
  String? respondedAt,
  String? agentOutcome,
  String? agentReason,
  String? agentQueuedAt,
}) =>
    {
      'id': 7,
      'workOrderId': 30,
      'reportId': 5,
      'reportDescription': 'Projector cuts out mid lecture',
      'asset': {
        'id': 2,
        'assetTag': 'PRJ-MAB101-01',
        'name': 'Ceiling projector',
        'assetCategoryId': 1,
        'roomId': 1,
        'manufacturer': null,
        'model': null,
        'installedOn': '2024-01-15',
        'warrantyExpiresOn': null,
        'status': 'Active',
        'createdAt': '2026-01-01T00:00:00Z',
        'updatedAt': '2026-01-01T00:00:00Z',
      },
      'workOrderResolutionNote': _note,
      'workOrderCompletedAt': '2026-09-15T10:00:00Z',
      'dueAt': '2026-09-20T10:00:00Z',
      'status': status,
      'isOverdue': false,
      'reporterConfirmed': confirmed,
      'reporterComment': null,
      'reporterRespondedAt': respondedAt,
      'agentOutcome': agentOutcome,
      'agentReason': agentReason,
      'agentEvidence': null,
      'newReportsSinceCompletion': null,
      'followUpWorkOrders': null,
      'processedAt': '2026-09-20T10:00:00Z',
      'agentQueuedAt': agentQueuedAt,
      'expiredReason': null,
      'createdAt': '2026-09-15T10:00:00Z',
      'updatedAt': '2026-09-20T10:00:00Z',
    };

Map<String, dynamic> _reportRow({Map<String, dynamic>? verification}) => {
      'id': 5,
      'reporterId': 3,
      'roomId': 1,
      'roomName': 'MAB101 · Lecture Hall A',
      'assetId': 2,
      'description': 'Projector cuts out mid lecture',
      'status': 'Closed',
      'unansweredQuestionCount': 0,
      'verification': verification,
      'createdAt': '2026-09-01T07:00:00Z',
      'updatedAt': '2026-09-15T07:00:00Z',
    };

Future<void> _pumpRouted(
  WidgetTester tester, {
  required ApiClient client,
  required String initialLocation,
}) async {
  final router = GoRouter(
    initialLocation: initialLocation,
    routes: [
      GoRoute(path: '/', builder: (_, __) => const Text('HOME')),
      GoRoute(path: '/reports', builder: (_, __) => const MyReportsScreen()),
      GoRoute(
        path: '/verifications',
        builder: (_, __) => const PendingConfirmationsScreen(),
        routes: [
          GoRoute(
            path: ':id',
            builder: (_, state) =>
                ConfirmFixScreen(checkId: int.tryParse(state.pathParameters['id'] ?? '')),
          ),
        ],
      ),
    ],
  );
  addTearDown(router.dispose);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [apiClientProvider.overrideWithValue(client)],
      child: MaterialApp.router(routerConfig: router),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('validateConfirmation', () {
    test('the yes/no has no default — unanswered is an error, not a "no"', () {
      expect(validateConfirmation(fixed: null, comment: ''), {'fixed': 'Choose yes or no.'});
      expect(validateConfirmation(fixed: false, comment: ''), isEmpty);
    });

    test('the comment is optional and stops at 300, measured trimmed', () {
      expect(validateConfirmation(fixed: true, comment: '  ${'a' * 300}  '), isEmpty);
      expect(
        validateConfirmation(fixed: true, comment: 'a' * 301),
        {'comment': 'Keep it to 300 characters.'},
      );
    });
  });

  group('VerificationApi', () {
    test('the pending list asks for AwaitingReporterResponse by NAME', () async {
      late Uri requested;
      final api = VerificationApi(_client((request) async {
        requested = request.url;
        return _json(_page([_listItem(7)]));
      }));

      final result = await api.pending();

      expect(requested.path, '/api/verifications');
      expect(requested.queryParameters['status'], 'AwaitingReporterResponse');
      expect(result.items.single.reportDescription, 'Projector cuts out mid lecture');
    });

    test('the answer is ONE post; a blank comment goes as null, a real one trimmed', () async {
      final requests = <http.Request>[];
      final api = VerificationApi(_client((request) async {
        requests.add(request);
        return http.Response('', 204);
      }));

      await api.confirm(7, fixed: false, comment: '   ');
      await api.confirm(8, fixed: true, comment: '  works now  ');

      expect(requests.map((r) => r.url.path), ['/api/verifications/7/confirm', '/api/verifications/8/confirm']);
      expect(jsonDecode(requests[0].body), {'confirmed': false, 'comment': null});
      expect(jsonDecode(requests[1].body), {'confirmed': true, 'comment': 'works now'});
    });
  });

  group('PendingConfirmationsScreen', () {
    testWidgets('nothing to confirm is an empty state, not an error', (tester) async {
      await _pumpRouted(
        tester,
        client: _client((_) async => _json(_page([]))),
        initialLocation: '/verifications',
      );

      expect(find.byType(EmptyView), findsOneWidget);
      expect(find.byType(ErrorView), findsNothing);
      expect(find.textContaining('Nothing to confirm right now'), findsOneWidget);
    });

    testWidgets('a failed request is an ErrorView with a retry', (tester) async {
      var calls = 0;
      await _pumpRouted(
        tester,
        client: _client((_) async {
          calls++;
          return calls == 1 ? _problem(500, 'Boom') : _json(_page([]));
        }),
        initialLocation: '/verifications',
      );

      expect(find.byType(ErrorView), findsOneWidget);
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(find.byType(EmptyView), findsOneWidget);
    });

    testWidgets('a check shows what was reported, the API\'s overdue flag, and opens the form',
        (tester) async {
      await _pumpRouted(
        tester,
        client: _client((request) async => request.url.path == '/api/verifications'
            ? _json(_page([_listItem(7, isOverdue: true)]))
            : _json(_detail())),
        initialLocation: '/verifications',
      );

      expect(find.text('Projector cuts out mid lecture'), findsOneWidget);
      expect(find.text('Waiting a while'), findsOneWidget);

      await tester.tap(find.text('Projector cuts out mid lecture'));
      await tester.pumpAndSettle();
      expect(find.text('Is the problem fixed?'), findsOneWidget);
    });
  });

  group('ConfirmFixScreen', () {
    testWidgets('shows the claim verbatim, then ONE yes/no and ONE capped text field — a form',
        (tester) async {
      await _pumpRouted(
        tester,
        client: _client((_) async => _json(_detail())),
        initialLocation: '/verifications/7',
      );

      expect(find.text('Projector cuts out mid lecture'), findsOneWidget);
      expect(find.text(_note), findsOneWidget);
      expect(find.textContaining('Completed'), findsOneWidget);

      expect(find.byType(SegmentedButton<bool>), findsOneWidget);
      // Exactly one text box, and it is the capped comment — not a message box.
      expect(find.byType(TextField), findsOneWidget);
      expect(tester.widget<TextField>(find.byType(TextField)).maxLength, 300);

      // Neither answer is chosen for the reporter.
      final toggle = tester.widget<SegmentedButton<bool>>(find.byType(SegmentedButton<bool>));
      expect(toggle.selected, isEmpty);
    });

    testWidgets('submitting without an answer is refused by validate() and nothing is sent',
        (tester) async {
      var posts = 0;
      await _pumpRouted(
        tester,
        client: _client((request) async {
          if (request.method == 'POST') posts++;
          return _json(_detail());
        }),
        initialLocation: '/verifications/7',
      );

      await tester.tap(find.text('Submit answer'));
      await tester.pumpAndSettle();

      expect(find.text('Choose yes or no.'), findsOneWidget);
      expect(posts, 0);
    });

    testWidgets('"no, still broken" goes in one POST, and the screen then shows Reopened',
        (tester) async {
      final posts = <http.Request>[];
      await _pumpRouted(
        tester,
        client: _client((request) async {
          if (request.method == 'POST') {
            posts.add(request);
            return http.Response('', 204);
          }
          // Before the answer the check waits on the reporter; after it, C# has reopened it
          // and handed it to the review in the same save.
          return posts.isEmpty
              ? _json(_detail())
              : _json(_detail(
                  status: 'Reopened',
                  confirmed: false,
                  respondedAt: '2026-09-24T09:00:00Z',
                  agentQueuedAt: '2026-09-24T09:00:00Z',
                ));
        }),
        initialLocation: '/verifications/7',
      );

      await tester.tap(find.text('No, still broken'));
      await tester.enterText(find.byType(TextField), 'Cut out twice on Monday.');
      await tester.tap(find.text('Submit answer'));
      await tester.pumpAndSettle();

      expect(posts, hasLength(1));
      expect(jsonDecode(posts.single.body), {'confirmed': false, 'comment': 'Cut out twice on Monday.'});

      // The form is gone — there is no second round — and the new status is shown.
      expect(find.byType(TextField), findsNothing);
      expect(find.text('Submit answer'), findsNothing);
      expect(find.text('Reopened'), findsOneWidget);
      expect(find.textContaining('The repair has been reopened'), findsOneWidget);
      expect(find.text('Your answer: No, still broken'), findsOneWidget);
      expect(find.textContaining('Sent for review'), findsOneWidget);
      // The comment is not replayed: the status is the record, not a transcript.
      expect(find.text('Cut out twice on Monday.'), findsNothing);
    });

    testWidgets("the review's later label is shown beside the status, as a review",
        (tester) async {
      await _pumpRouted(
        tester,
        client: _client((_) async => _json(_detail(
              status: 'Confirmed',
              confirmed: true,
              respondedAt: '2026-09-22T09:00:00Z',
              agentQueuedAt: '2026-09-22T09:00:00Z',
              agentOutcome: 'reopen',
              agentReason: 'Note admits a temporary fix; a new report was filed since.',
            ))),
        initialLocation: '/verifications/7',
      );

      // The status is still the reporter's answer ...
      expect(find.text('Confirmed'), findsOneWidget);
      // ... and the agent's opinion sits beside it, with its reason.
      expect(find.text('Review: reopen'), findsOneWidget);
      expect(find.textContaining('flagged it to facilities'), findsOneWidget);
      expect(find.text('Note admits a temporary fix; a new report was filed since.'), findsOneWidget);
    });

    testWidgets('a 409 says why and shows the check as it really is now', (tester) async {
      var answeredElsewhere = false;
      await _pumpRouted(
        tester,
        client: _client((request) async {
          if (request.method == 'POST') {
            answeredElsewhere = true;
            return _problem(409, 'Verification check 7 has already been answered.');
          }
          return answeredElsewhere
              ? _json(_detail(status: 'Confirmed', confirmed: true, respondedAt: '2026-09-24T08:00:00Z'))
              : _json(_detail());
        }),
        initialLocation: '/verifications/7',
      );

      await tester.tap(find.text("Yes, it's fixed"));
      await tester.tap(find.text('Submit answer'));
      await tester.pumpAndSettle();

      expect(find.text('Verification check 7 has already been answered.'), findsOneWidget);
      expect(find.text('Confirmed'), findsOneWidget);
      expect(find.byType(TextField), findsNothing);
    });

    testWidgets("someone else's check is 'Not your report', not a generic error",
        (tester) async {
      await _pumpRouted(
        tester,
        client: _client((_) async => _problem(403, 'Not your report')),
        initialLocation: '/verifications/7',
      );

      expect(find.text('Not your report'), findsOneWidget);
      expect(find.text('Try again'), findsNothing);
    });
  });

  group('MyReportsScreen', () {
    testWidgets('a reopened repair shows on the report, with the review flag beside it',
        (tester) async {
      await _pumpRouted(
        tester,
        client: _client((request) async => request.url.path == '/api/reports'
            ? _json(_page([
                _reportRow(verification: {'id': 7, 'status': 'Reopened', 'agentOutcome': 'escalate'}),
              ]))
            : _json(_detail(status: 'Reopened', confirmed: false, respondedAt: '2026-09-24T09:00:00Z'))),
        initialLocation: '/reports',
      );

      // The report's own status is Closed, and says nothing about the repair — the line does.
      expect(find.text('Closed'), findsOneWidget);
      expect(find.text('Reopened'), findsOneWidget);
      expect(find.text('Review: escalate'), findsOneWidget);
      expect(find.textContaining('You said it is still broken'), findsOneWidget);

      await tester.tap(find.text('Projector cuts out mid lecture'));
      await tester.pumpAndSettle();
      expect(find.text('Your answer: No, still broken'), findsOneWidget);
    });

    testWidgets('a repair waiting on the reporter is offered as a question', (tester) async {
      await _pumpRouted(
        tester,
        client: _client((request) async => request.url.path == '/api/reports'
            ? _json(_page([
                _reportRow(verification: {
                  'id': 7,
                  'status': 'AwaitingReporterResponse',
                  'agentOutcome': null,
                }),
              ]))
            : _json(_detail())),
        initialLocation: '/reports',
      );

      expect(find.text('Waiting on you'), findsOneWidget);
      await tester.tap(find.text('Answer'));
      await tester.pumpAndSettle();
      expect(find.text('Is the problem fixed?'), findsOneWidget);
    });

    testWidgets('a report with no repair yet carries no repair line', (tester) async {
      await _pumpRouted(
        tester,
        client: _client((_) async => _json(_page([_reportRow()]))),
        initialLocation: '/reports',
      );

      expect(find.text('Repair'), findsNothing);
    });
  });
}
