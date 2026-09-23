import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/paged_result.dart';
import '../../core/providers.dart';
import 'clarification.dart';
import 'report.dart';
import 'room.dart';

/// Every call the reports feature makes to the API. Screens never call the client directly.
class ReportsApi {
  ReportsApi(this._client);

  final ApiClient _client;

  /// GET /api/rooms -> 200 with a list of RoomDto. `buildingId` narrows it when a building
  /// picker is added later.
  Future<List<Room>> rooms({int? buildingId}) async {
    final json = await _client.get(
      '/api/rooms',
      query: buildingId == null ? null : {'buildingId': '$buildingId'},
    ) as List<dynamic>;

    return json
        .map((item) => Room.fromJson(item as Map<String, dynamic>))
        .toList(growable: false);
  }

  /// POST /api/reports -> 201 with the created report. Returns its id, which the photo
  /// upload needs: a photo is attached to a report that already exists.
  ///
  /// The reporter is NOT sent: the API reads it from the `sub` claim of the bearer token
  /// ApiClient attaches, so a client cannot file a report as somebody else. Filing also
  /// raises the agent workflow on the server, but that happens in the background — this
  /// call returns as soon as the report is stored, not when the agent has finished.
  Future<int> submit({required String description, required int roomId}) async {
    final json = await _client.post(
      '/api/reports',
      body: {
        'description': description,
        'roomId': roomId,
        // Hook: the QR scanner fills in an asset code. Not sent while it would always be
        // null — see SubmitReportScreen.
      },
    ) as Map<String, dynamic>;

    return json['id'] as int;
  }

  /// POST /api/reports/{id}/photo — multipart, one file part named `photo`. Returns the
  /// stored photo's URL.
  ///
  /// Only the reporter who filed the report may attach one, and the API checks the type,
  /// the size and the file's first bytes before anything is stored. A 503 means storage
  /// could not be reached and nothing was attached; the report itself is unchanged.
  Future<String> uploadPhoto(
    int reportId, {
    required Uint8List bytes,
    required String contentType,
    void Function(int sent, int total)? onProgress,
  }) async {
    final json = await _client.postFile(
      '/api/reports/$reportId/photo',
      field: 'photo',
      bytes: bytes,
      contentType: contentType,
      // Required for the part to bind as a file; the API never reads it. A fixed name
      // rather than the one on the phone, so no device file name leaves the phone.
      filename: contentType == 'image/png' ? 'photo.png' : 'photo.jpg',
      onProgress: onProgress,
    ) as Map<String, dynamic>;

    return json['photoUrl'] as String;
  }

  /// GET /api/reports — one page.
  ///
  /// WHO SEES WHAT IS NOT A PARAMETER. The API scopes a Reporter to their own reports from
  /// the token's role; there is nothing this call could send that widens it. Empty filters
  /// are left out rather than sent blank, and `status` is sent by enum NAME.
  Future<PagedResult<ReportListItem>> list({
    String search = '',
    String? status,
    int page = 1,
    int pageSize = 20,
  }) async {
    final json = await _client.get(
      '/api/reports',
      query: {
        if (search.trim().isNotEmpty) 'search': search.trim(),
        if (status != null) 'status': status,
        'page': '$page',
        'pageSize': '$pageSize',
      },
    ) as Map<String, dynamic>;

    return PagedResult.fromJson(json, ReportListItem.fromJson);
  }

  /// GET /api/reports/{id}/clarifications — the questions in the order the form renders
  /// them, each with its answer once one has been given. An empty list is a report nobody
  /// needed to ask about, not an error.
  Future<List<ClarificationQuestion>> clarifications(int reportId) async {
    final json = await _client.get('/api/reports/$reportId/clarifications') as List<dynamic>;

    return json
        .map((item) => ClarificationQuestion.fromJson(item as Map<String, dynamic>))
        .toList(growable: false);
  }

  /// POST /api/reports/{id}/clarifications -> 204.
  ///
  /// THE WHOLE FORM IN ONE REQUEST, and then the exchange is over: no reply comes back and
  /// there is no follow-up round. [answers] is question id -> answer text, and every
  /// question on the report must be in it.
  Future<void> submitAnswers(int reportId, Map<int, String> answers) async {
    await _client.post(
      '/api/reports/$reportId/clarifications',
      body: {
        'answers': [
          for (final entry in answers.entries)
            {'questionId': entry.key, 'answerText': entry.value},
        ],
      },
    );
  }
}

final reportsApiProvider = Provider<ReportsApi>((ref) {
  return ReportsApi(ref.watch(apiClientProvider));
});

/// The room picker's data. Rebuilt with `ref.invalidate(roomsProvider)` on retry.
final roomsProvider = FutureProvider<List<Room>>((ref) {
  return ref.watch(reportsApiProvider).rooms();
});

/// What the "My reports" list is showing. A record, so two equal queries are the same
/// provider and a changed filter is a new request.
typedef ReportQuery = ({String search, String? status, int page});

/// One page of reports. autoDispose so coming back to the list reads it fresh — the agent
/// may have asked questions since. Invalidate the whole family after answering.
final reportsPageProvider =
    FutureProvider.autoDispose.family<PagedResult<ReportListItem>, ReportQuery>((ref, query) {
  return ref.watch(reportsApiProvider).list(
        search: query.search,
        status: query.status,
        page: query.page,
      );
});

final clarificationsProvider =
    FutureProvider.autoDispose.family<List<ClarificationQuestion>, int>((ref, reportId) {
  return ref.watch(reportsApiProvider).clarifications(reportId);
});
