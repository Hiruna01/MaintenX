import '../assets/asset.dart' show splitPascalCase;
import '../verification/verification.dart' show ReportVerification;

/// Mirrors the API's `ReportStatus` enum. Matched by NAME, never by ordinal — these are the
/// exact strings the API sends and the `status` filter accepts.
///
/// Deliberately not the workflow's states: a workflow describes one agent run and can end
/// in Failed, while this says where the FAULT has got to.
class ReportStatuses {
  const ReportStatuses._();

  static const String submitted = 'Submitted';
  static const String awaitingClarification = 'AwaitingClarification';
  static const String clarified = 'Clarified';
  static const String diagnosed = 'Diagnosed';
  static const String workOrderRaised = 'WorkOrderRaised';
  static const String closed = 'Closed';

  /// In lifecycle order, for the filter chips.
  static const List<String> all = [
    submitted,
    awaitingClarification,
    clarified,
    diagnosed,
    workOrderRaised,
    closed,
  ];

  static String label(String status) => splitPascalCase(status);
}

const List<String> _months = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
];

/// An API timestamp (`CreatedAt`, UTC) in the phone's local time: "23 Sep 2026, 09:05".
///
/// Unlike a `DateOnly`, this IS an instant, so converting it to local time is correct —
/// a report filed at 00:30 in Colombo was filed on that local day.
String formatTimestamp(String? value) {
  final parsed = value == null ? null : DateTime.tryParse(value);
  if (parsed == null) return '—';
  final local = parsed.toLocal();
  String two(int n) => n.toString().padLeft(2, '0');
  return '${local.day} ${_months[local.month - 1]} ${local.year}, '
      '${two(local.hour)}:${two(local.minute)}';
}

/// Mirrors the API's `ReportListItemDto` — one row, and nothing that would cost a query
/// per row. The questions themselves are not here; [unansweredQuestionCount] is the one
/// thing a list needs to say about them: "this is waiting on you".
class ReportListItem {
  const ReportListItem({
    required this.id,
    required this.roomName,
    required this.description,
    required this.status,
    required this.unansweredQuestionCount,
    required this.verification,
    required this.createdAt,
  });

  final int id;
  final String roomName;
  final String description;
  final String status;
  final int unansweredQuestionCount;

  /// The latest check on this report's repair. The report's own status stops at
  /// WorkOrderRaised or Closed and cannot say whether the repair held; this can. Null when
  /// no repair has been completed yet.
  final ReportVerification? verification;

  final String createdAt;

  /// Whether the clarification form is open for this report. Both halves, because the
  /// status is what the API will accept an answer against and the count is whether there
  /// is anything left to answer.
  bool get isWaitingOnReporter =>
      status == ReportStatuses.awaitingClarification && unansweredQuestionCount > 0;

  factory ReportListItem.fromJson(Map<String, dynamic> json) {
    return ReportListItem(
      id: json['id'] as int,
      roomName: json['roomName'] as String,
      description: json['description'] as String,
      status: json['status'] as String,
      unansweredQuestionCount: json['unansweredQuestionCount'] as int,
      verification: json['verification'] == null
          ? null
          : ReportVerification.fromJson(json['verification'] as Map<String, dynamic>),
      createdAt: json['createdAt'] as String,
    );
  }
}
