import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../reports/report.dart';
import '../reports/reports_api.dart';
import '../verification/verification_api.dart';
import '../workorders/work_orders_api.dart';

// The numbers on the Home card. Each is the API's `totalCount` for one existing list
// request, asked for as a page of ONE — the same approach as the web dashboard's status
// counts. Nothing here counts, adds or decides anything: whose reports, checks and jobs
// they are is the API's scope, from the token, and a filter by status NAME is all we send.

/// Reports sitting at AwaitingClarification — the ones with questions for the reporter.
final reportsAwaitingAnswerCountProvider = FutureProvider.autoDispose<int>((ref) async {
  final page = await ref
      .watch(reportsApiProvider)
      .list(status: ReportStatuses.awaitingClarification, pageSize: 1);
  return page.totalCount;
});

/// Repair checks waiting on the reporter's yes/no.
final repairsToConfirmCountProvider = FutureProvider.autoDispose<int>((ref) async {
  final page = await ref.watch(verificationApiProvider).pending(pageSize: 1);
  return page.totalCount;
});

/// A technician's jobs in one status, by NAME.
final jobsInStatusCountProvider =
    FutureProvider.autoDispose.family<int, String>((ref, status) async {
  final page = await ref.watch(workOrdersApiProvider).list(status: status, pageSize: 1);
  return page.totalCount;
});
