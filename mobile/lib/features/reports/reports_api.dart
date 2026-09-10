import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/providers.dart';
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

  /// POST /api/reports -> 201 with the created report.
  ///
  /// The reporter is NOT sent: the API reads it from the `sub` claim of the bearer token
  /// ApiClient attaches, so a client cannot file a report as somebody else. Filing also
  /// raises the agent workflow on the server, but that happens in the background — this
  /// call returns as soon as the report is stored, not when the agent has finished.
  Future<void> submit({required String description, required int roomId}) async {
    await _client.post(
      '/api/reports',
      body: {
        'description': description,
        'roomId': roomId,
        // Hook: a photo is uploaded separately (multipart) and referenced here, and the QR
        // scanner fills in an asset code. Neither field is sent while it would always be
        // null — see SubmitReportScreen.
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
