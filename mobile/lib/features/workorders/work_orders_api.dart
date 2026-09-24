import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/paged_result.dart';
import '../../core/providers.dart';
import '../auth/auth_controller.dart';
import 'completion.dart';
import 'work_order.dart';

/// Every call the work orders feature makes to the API. Screens never call the client
/// directly.
class WorkOrdersApi {
  WorkOrdersApi(this._client);

  final ApiClient _client;

  /// GET /api/workorders — one page, newest first.
  ///
  /// WHO SEES WHAT IS NOT A PARAMETER. The API scopes a Technician to the orders assigned
  /// to them from the token's role, and ignores any `technicianId` that would widen it —
  /// so none is sent. `status` goes by enum NAME.
  Future<PagedResult<WorkOrderListItem>> list({
    String? status,
    int page = 1,
    int pageSize = 20,
  }) async {
    final json = await _client.get(
      '/api/workorders',
      query: {
        if (status != null) 'status': status,
        'page': '$page',
        'pageSize': '$pageSize',
      },
    ) as Map<String, dynamic>;

    return PagedResult.fromJson(json, WorkOrderListItem.fromJson);
  }

  /// GET /api/workorders/{id}. Someone else's job is the API's 403, told apart from a 404.
  Future<WorkOrderDetail> byId(int id) async {
    final json = await _client.get('/api/workorders/$id') as Map<String, dynamic>;
    return WorkOrderDetail.fromJson(json);
  }

  /// POST /api/workorders/{id}/photo — multipart, one file part named `photo`. Returns the
  /// stored URL, which the API has already recorded on the order.
  ///
  /// THE SAME UPLOAD PATH AS A REPORT PHOTO: `ApiClient.postFile` here, and on the server
  /// `ImageUploadRules` then `IFileStorageService`. Only the assigned technician, and only
  /// while the job is live work — so it is called BEFORE [complete], never after.
  Future<String> uploadCompletionPhoto(
    int id, {
    required Uint8List bytes,
    required String contentType,
    void Function(int sent, int total)? onProgress,
  }) async {
    final json = await _client.postFile(
      '/api/workorders/$id/photo',
      field: 'photo',
      bytes: bytes,
      contentType: contentType,
      // Required for the part to bind as a file; the API never reads it.
      filename: contentType == 'image/png' ? 'photo.png' : 'photo.jpg',
      onProgress: onProgress,
    ) as Map<String, dynamic>;

    return json['completionPhotoUrl'] as String;
  }

  /// POST /api/workorders/{id}/complete -> 204.
  ///
  /// One request, one transaction on the server: the order Completed, a ServiceRecord
  /// appended with [resolutionNote] verbatim, a verification check raised. No
  /// `completionPhotoUrl` is sent — the photo was attached by [uploadCompletionPhoto], and
  /// the API keeps it when the field is left out. `outcome` goes by enum NAME.
  Future<void> complete(
    int id, {
    required String outcome,
    required String actualCost,
    required String resolutionNote,
  }) async {
    await _client.post(
      '/api/workorders/$id/complete',
      body: {
        'actualCost': costForApi(actualCost),
        'outcome': outcome,
        'resolutionNote': resolutionNote.trim(),
      },
    );
  }
}

final workOrdersApiProvider = Provider<WorkOrdersApi>((ref) {
  return WorkOrdersApi(ref.watch(apiClientProvider));
});

/// Who is signed in, for "is this job mine to complete". Only decides whether a button is
/// offered — the API checks the assignment again on every write.
final currentUserIdProvider = Provider<int?>((ref) {
  return ref.watch(authControllerProvider.select((state) => state.user?.id));
});

/// What "My jobs" is showing. A record, so two equal queries are the same provider.
typedef JobsQuery = ({String? status, int page});

/// One page of jobs. autoDispose so coming back to the list reads it fresh — a manager may
/// have assigned or booked something since. Invalidate the family after completing.
final jobsPageProvider =
    FutureProvider.autoDispose.family<PagedResult<WorkOrderListItem>, JobsQuery>((ref, query) {
  return ref.watch(workOrdersApiProvider).list(status: query.status, page: query.page);
});

final workOrderDetailProvider =
    FutureProvider.autoDispose.family<WorkOrderDetail, int>((ref, id) {
  return ref.watch(workOrdersApiProvider).byId(id);
});
