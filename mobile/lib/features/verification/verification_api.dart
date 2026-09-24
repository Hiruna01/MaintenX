import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/paged_result.dart';
import '../../core/providers.dart';
import 'verification.dart';

/// Every call the verification feature makes to the API. Screens never call the client
/// directly.
class VerificationApi {
  VerificationApi(this._client);

  final ApiClient _client;

  /// GET /api/verifications?status=AwaitingReporterResponse — the checks waiting on an
  /// answer, latest due first (the API's default sort).
  ///
  /// WHOSE CHECKS IS NOT A PARAMETER. The API scopes a Reporter to the checks on reports they
  /// filed, from the token; nothing sent here widens it. The status goes by NAME.
  Future<PagedResult<VerificationListItem>> pending({int page = 1, int pageSize = 20}) async {
    final json = await _client.get(
      '/api/verifications',
      query: {
        'status': VerificationStatuses.awaitingReporterResponse,
        'page': '$page',
        'pageSize': '$pageSize',
      },
    ) as Map<String, dynamic>;

    return PagedResult.fromJson(json, VerificationListItem.fromJson);
  }

  /// GET /api/verifications/{id}. Someone else's check is a 403, told apart from a 404.
  Future<VerificationDetail> detail(int id) async {
    final json = await _client.get('/api/verifications/$id') as Map<String, dynamic>;
    return VerificationDetail.fromJson(json);
  }

  /// POST /api/verifications/{id}/confirm -> 204.
  ///
  /// A YES/NO AND AN OPTIONAL CAPPED COMMENT, ONCE. No reply comes back and there is no
  /// second round: a second answer is a 409. A blank comment is sent as null rather than as
  /// an empty string, so "said nothing" is stored as nothing.
  Future<void> confirm(int id, {required bool fixed, String? comment}) async {
    final trimmed = comment?.trim() ?? '';
    await _client.post(
      '/api/verifications/$id/confirm',
      body: {
        'confirmed': fixed,
        'comment': trimmed.isEmpty ? null : trimmed,
      },
    );
  }
}

final verificationApiProvider = Provider<VerificationApi>((ref) {
  return VerificationApi(ref.watch(apiClientProvider));
});

/// One page of checks waiting on the reporter. autoDispose so coming back to the list reads
/// it fresh — the sweep may have asked something since. Invalidate the family after answering.
final pendingVerificationsProvider =
    FutureProvider.autoDispose.family<PagedResult<VerificationListItem>, int>((ref, page) {
  return ref.watch(verificationApiProvider).pending(page: page);
});

final verificationDetailProvider =
    FutureProvider.autoDispose.family<VerificationDetail, int>((ref, id) {
  return ref.watch(verificationApiProvider).detail(id);
});
