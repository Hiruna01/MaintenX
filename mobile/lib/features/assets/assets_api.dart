import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/providers.dart';
import 'asset.dart';

/// Every call the assets feature makes to the API. Screens never call the client directly.
///
/// Read-only on purpose: registering and editing assets is an Admin job done from the web
/// client. The phone's job is to identify the machine in front of it.
class AssetsApi {
  AssetsApi(this._client);

  final ApiClient _client;

  /// GET /api/assets/by-tag/{assetTag} — the QR path.
  ///
  /// Returns **null for a 404**, because an unknown tag is an ordinary answer, not an
  /// error: a sticker from some other system, or a QR code that was never ours, is a miss
  /// and the scanner says so. Every other failure — no network, a timeout, a 500 — still
  /// throws [ApiException], so the screen can tell "we asked and it isn't registered" from
  /// "we could not ask".
  ///
  /// The tag is path-encoded because it is whatever the camera read, and a QR code can
  /// carry a URL full of slashes.
  Future<AssetDetail?> findByTag(String assetTag) async {
    try {
      final json = await _client.get('/api/assets/by-tag/${Uri.encodeComponent(assetTag)}');
      return AssetDetail.fromJson(json as Map<String, dynamic>);
    } on ApiException catch (error) {
      if (error.statusCode == 404) return null;
      rethrow;
    }
  }

  /// GET /api/assets/{id} — the asset, its room and category, and its full history.
  Future<AssetDetail> byId(int id) async {
    final json = await _client.get('/api/assets/$id');
    return AssetDetail.fromJson(json as Map<String, dynamic>);
  }

  /// GET /api/assets/{id}/failure-summary — counts and date comparisons computed in C#.
  Future<FailureSummary> failureSummary(int id) async {
    final json = await _client.get('/api/assets/$id/failure-summary');
    return FailureSummary.fromJson(json as Map<String, dynamic>);
  }
}

final assetsApiProvider = Provider<AssetsApi>((ref) {
  return AssetsApi(ref.watch(apiClientProvider));
});

/// One asset by id. autoDispose so leaving the screen drops it, and coming back — after a
/// technician has added a visit, say — reads it fresh. Retry with `ref.invalidate`.
final assetDetailProvider = FutureProvider.autoDispose.family<AssetDetail, int>((ref, id) {
  return ref.watch(assetsApiProvider).byId(id);
});

final failureSummaryProvider =
    FutureProvider.autoDispose.family<FailureSummary, int>((ref, id) {
  return ref.watch(assetsApiProvider).failureSummary(id);
});
