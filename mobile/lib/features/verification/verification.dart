import '../assets/asset.dart' show splitPascalCase;

/// Mirrors the API's `VerificationStatus` enum. Matched by NAME, never by ordinal — these are
/// the exact strings the API sends and the `status` filter accepts.
///
/// Every move between them is C# in `VerificationService`. The reporter's answer sets
/// Confirmed or Reopened; nothing on the phone, and nothing the agent says, sets any of them.
class VerificationStatuses {
  const VerificationStatuses._();

  static const String pending = 'Pending';
  static const String awaitingReporterResponse = 'AwaitingReporterResponse';
  static const String confirmed = 'Confirmed';
  static const String reopened = 'Reopened';
  static const String escalated = 'Escalated';
  static const String expired = 'Expired';

  static String label(String status) => switch (status) {
        // "Awaiting reporter response" is the system's view; on the reporter's own phone
        // the same state is a question waiting on them.
        awaitingReporterResponse => 'Waiting on you',
        _ => splitPascalCase(status),
      };
}

/// The verification agent's label, as stored in `VerificationCheck.AgentOutcome`.
///
/// Strings, not an enum, on purpose — there is no C# enum for it either. It is the model's
/// opinion, shown beside the status and never acted on. An unknown value is shown raw.
class AgentOutcomes {
  const AgentOutcomes._();

  static const String confirm = 'confirm';
  static const String reopen = 'reopen';
  static const String escalate = 'escalate';

  /// Whether the agent thinks the repair did NOT hold — the case a reporter should see on
  /// their report even when their own answer was yes, or they never gave one.
  static bool flagsFollowUp(String? outcome) => outcome == reopen || outcome == escalate;
}

/// One sentence, for the reporter, saying where the check on their repair has got to. Read
/// from the status NAME; the rules that produced the status are the API's.
String describeForReporter(String status) => switch (status) {
      VerificationStatuses.pending =>
        'Repaired. In a few days we will ask you whether the repair held.',
      VerificationStatuses.awaitingReporterResponse =>
        'Is the problem fixed? Facilities are waiting on your answer.',
      VerificationStatuses.confirmed => 'You confirmed the repair held.',
      VerificationStatuses.reopened =>
        'You said it is still broken. The repair has been reopened and facilities have been told.',
      VerificationStatuses.escalated =>
        'Escalated to the facilities manager — this fault has come back before.',
      VerificationStatuses.expired => 'No answer was recorded on whether the repair held.',
      _ => 'Verification: $status',
    };

/// One sentence for the agent's label. Worded as a review a person will act on, because
/// that is all it is: nothing in the system changes on it by itself.
String describeAgentOutcome(String outcome) => switch (outcome) {
      AgentOutcomes.confirm => 'The automated review found nothing to suggest the fault is back.',
      AgentOutcomes.reopen =>
        'The automated review thinks this repair did not hold, and has flagged it to facilities.',
      AgentOutcomes.escalate =>
        'The automated review has flagged this as a repeat fault for the facilities manager.',
      _ => 'The automated review labelled this "$outcome".',
    };

/// The longest comment the API accepts — `ReporterConfirmationDto.Comment`, `[StringLength(300)]`.
const int maxCommentLength = 300;

/// Mirrors the API's `VerificationCheckDto` — one row of the pending list.
///
/// [reportDescription] is on the row because a reporter does not know an asset tag; what
/// they reported is how they recognise which repair they are being asked about.
class VerificationListItem {
  const VerificationListItem({
    required this.id,
    required this.reportId,
    required this.reportDescription,
    required this.workOrderCompletedAt,
    required this.assetTag,
    required this.dueAt,
    required this.status,
    required this.isOverdue,
  });

  final int id;
  final int reportId;
  final String reportDescription;
  final String? workOrderCompletedAt;
  final String assetTag;
  final String dueAt;
  final String status;

  /// Decided by the API (`VerificationService.IsOverdue`). The phone only colours it and
  /// never compares a date itself.
  final bool isOverdue;

  factory VerificationListItem.fromJson(Map<String, dynamic> json) {
    return VerificationListItem(
      id: json['id'] as int,
      reportId: json['reportId'] as int,
      reportDescription: json['reportDescription'] as String,
      workOrderCompletedAt: json['workOrderCompletedAt'] as String?,
      assetTag: json['assetTag'] as String,
      dueAt: json['dueAt'] as String,
      status: json['status'] as String,
      isOverdue: json['isOverdue'] as bool,
    );
  }
}

/// Mirrors the parts of the API's `VerificationDetailDto` a reporter's screen reads.
///
/// The manager-only fields (`newReportsSinceCompletion`, `followUpWorkOrders`) are null for
/// a reporter and are not read here at all.
class VerificationDetail {
  const VerificationDetail({
    required this.id,
    required this.reportId,
    required this.reportDescription,
    required this.assetName,
    required this.assetTag,
    required this.resolutionNote,
    required this.completedAt,
    required this.dueAt,
    required this.status,
    required this.reporterConfirmed,
    required this.reporterRespondedAt,
    required this.agentOutcome,
    required this.agentReason,
    required this.agentQueuedAt,
  });

  final int id;
  final int reportId;
  final String reportDescription;
  final String assetName;
  final String assetTag;

  /// The technician's account of the work, verbatim. Null on older orders.
  final String? resolutionNote;
  final String? completedAt;
  final String dueAt;
  final String status;

  /// Null is "not answered", never "no" — the same as the API's `bool?`.
  final bool? reporterConfirmed;
  final String? reporterRespondedAt;

  final String? agentOutcome;
  final String? agentReason;

  /// When the answer was handed to the automated review. Set together with the answer.
  final String? agentQueuedAt;

  bool get isAnswered => reporterRespondedAt != null;

  /// Whether the form is open: the API accepts an answer only in this state, and only once.
  bool get isAwaitingAnswer =>
      status == VerificationStatuses.awaitingReporterResponse && !isAnswered;

  factory VerificationDetail.fromJson(Map<String, dynamic> json) {
    final asset = json['asset'] as Map<String, dynamic>;
    return VerificationDetail(
      id: json['id'] as int,
      reportId: json['reportId'] as int,
      reportDescription: json['reportDescription'] as String,
      assetName: asset['name'] as String,
      assetTag: asset['assetTag'] as String,
      resolutionNote: json['workOrderResolutionNote'] as String?,
      completedAt: json['workOrderCompletedAt'] as String?,
      dueAt: json['dueAt'] as String,
      status: json['status'] as String,
      reporterConfirmed: json['reporterConfirmed'] as bool?,
      reporterRespondedAt: json['reporterRespondedAt'] as String?,
      agentOutcome: json['agentOutcome'] as String?,
      agentReason: json['agentReason'] as String?,
      agentQueuedAt: json['agentQueuedAt'] as String?,
    );
  }
}

/// Mirrors the API's `ReportVerificationDto` — the latest check on a report, carried on its
/// list row so the report says whether its repair held. Null on the row means no repair has
/// been completed yet.
class ReportVerification {
  const ReportVerification({
    required this.id,
    required this.status,
    required this.agentOutcome,
  });

  final int id;
  final String status;
  final String? agentOutcome;

  bool get isWaitingOnReporter => status == VerificationStatuses.awaitingReporterResponse;

  factory ReportVerification.fromJson(Map<String, dynamic> json) {
    return ReportVerification(
      id: json['id'] as int,
      status: json['status'] as String,
      agentOutcome: json['agentOutcome'] as String?,
    );
  }
}

/// The form's validate(): a message per field, keyed by field name — the same shape as every
/// other form in both clients. Empty means the form may be sent.
///
/// [fixed] has no default. Null is "not answered", and sending it as `false` would reopen a
/// repair nobody said had failed — the reason the API's `Confirmed` is `[Required] bool?`.
///
/// The comment is measured trimmed, because trimmed is what is sent, and in UTF-16 code units
/// (Dart's `String.length`), the way the API counts.
Map<String, String> validateConfirmation({required bool? fixed, required String comment}) {
  final errors = <String, String>{};
  if (fixed == null) {
    errors['fixed'] = 'Choose yes or no.';
  }
  if (comment.trim().length > maxCommentLength) {
    errors['comment'] = 'Keep it to $maxCommentLength characters.';
  }
  return errors;
}
