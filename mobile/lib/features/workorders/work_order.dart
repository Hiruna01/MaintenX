import '../assets/asset.dart' show splitPascalCase;
import '../reports/room.dart';

/// Mirrors the API's `WorkOrderStatus` enum. Matched by NAME, never by ordinal — these are
/// the exact strings the API sends and the `status` filter accepts.
class WorkOrderStatuses {
  const WorkOrderStatuses._();

  static const String draft = 'Draft';
  static const String awaitingApproval = 'AwaitingApproval';
  static const String approved = 'Approved';
  static const String rejected = 'Rejected';
  static const String scheduled = 'Scheduled';
  static const String inProgress = 'InProgress';
  static const String completed = 'Completed';
  static const String cancelled = 'Cancelled';

  /// The statuses a technician's own job can be in, for the filter chips. An order is only
  /// assignable once it has cleared approval, so Draft, AwaitingApproval and Rejected never
  /// reach a technician's queue, and a chip for them would always come back empty.
  static const List<String> technicianFilters = [
    approved,
    scheduled,
    inProgress,
    completed,
    cancelled,
  ];

  /// Live work — the statuses the API will complete (and attach a photo to). Offering the
  /// Complete button on these and nothing else is a courtesy; the API is the rule.
  static const Set<String> completable = {approved, scheduled, inProgress};

  static String label(String status) => splitPascalCase(status);
}

/// Mirrors the API's `WorkOrderStrategy` enum, by NAME.
class WorkOrderStrategies {
  const WorkOrderStrategies._();

  static String label(String strategy) => splitPascalCase(strategy);
}

const List<String> _months = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
];
const List<String> _weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

/// A money amount as the API sent it, for display: "Rs 45,000" or "Rs 1,250.50".
///
/// FORMATS AND NOTHING ELSE — the same rule as the web client's `formatMoney`. Nothing on
/// the phone adds, rounds or compares money to decide anything; whether an estimate needed a
/// manager is the API's arithmetic.
String formatMoney(num? value) {
  if (value == null) return '—';
  final whole = value == value.truncate();
  final text = whole ? value.truncate().toString() : value.toStringAsFixed(2);
  final parts = text.split('.');
  final grouped = parts[0].replaceAllMapped(RegExp(r'\B(?=(\d{3})+(?!\d))'), (_) => ',');
  return 'Rs $grouped${parts.length > 1 ? '.${parts[1]}' : ''}';
}

/// A booked visit as "Mon 28 Sep, 09:00–10:30", in the phone's local time.
///
/// Slots are INSTANTS (UTC with a `Z`), so converting them to local time is correct —
/// unlike a `DateOnly`. A technician's phone on campus is on campus time.
String formatSlot(String startsAt, String endsAt) {
  final start = DateTime.tryParse(startsAt)?.toLocal();
  final end = DateTime.tryParse(endsAt)?.toLocal();
  if (start == null || end == null) return '—';
  String two(int n) => n.toString().padLeft(2, '0');
  final day = '${_weekdays[start.weekday - 1]} ${start.day} ${_months[start.month - 1]}';
  return '$day, ${two(start.hour)}:${two(start.minute)}–${two(end.hour)}:${two(end.minute)}';
}

/// Mirrors the API's `WorkOrderDto` — one row of the list, and nothing that would cost a
/// query per row. The room, the slots and the diagnosis are on the detail.
class WorkOrderListItem {
  const WorkOrderListItem({
    required this.id,
    required this.assetTag,
    required this.status,
    required this.strategy,
    required this.createdAt,
    required this.completedAt,
  });

  final int id;
  final String assetTag;
  final String status;
  final String strategy;
  final String createdAt;
  final String? completedAt;

  factory WorkOrderListItem.fromJson(Map<String, dynamic> json) {
    return WorkOrderListItem(
      id: json['id'] as int,
      assetTag: json['assetTag'] as String,
      status: json['status'] as String,
      strategy: json['strategy'] as String,
      createdAt: json['createdAt'] as String,
      completedAt: json['completedAt'] as String?,
    );
  }
}

/// One booked visit — time that IS booked, not an offer.
class ScheduledSlot {
  const ScheduledSlot({required this.id, required this.startsAt, required this.endsAt});

  final int id;
  final String startsAt;
  final String endsAt;

  String get label => formatSlot(startsAt, endsAt);

  factory ScheduledSlot.fromJson(Map<String, dynamic> json) {
    return ScheduledSlot(
      id: json['id'] as int,
      startsAt: json['startsAt'] as String,
      endsAt: json['endsAt'] as String,
    );
  }
}

/// One candidate cause, with the evidence the agent cited for it — shown verbatim, because
/// the evidence is where it names the dated service visits it is going on.
class DiagnosisHypothesis {
  const DiagnosisHypothesis({
    required this.cause,
    required this.confidence,
    required this.evidence,
  });

  final String cause;

  /// high / medium / low, as the agent wrote it. A string, not an enum: it is advice.
  final String confidence;
  final List<String> evidence;

  factory DiagnosisHypothesis.fromJson(Map<String, dynamic> json) {
    return DiagnosisHypothesis(
      cause: json['cause'] as String,
      confidence: json['confidence'] as String,
      evidence: (json['evidence'] as List<dynamic>).cast<String>(),
    );
  }
}

/// Mirrors the API's `AgentDiagnosisDto`: the diagnostic agent's latest answer for the
/// report. ADVICE, NEVER A DECISION — nothing on the phone acts on it.
///
/// Three ways it can arrive, and the screen says which: a readable diagnosis; a run that
/// failed ([failed], with [errorMessage]); a run that said Ok but whose output could not be
/// read ([outputReadable] false). A report never diagnosed at all is a null diagnosis on
/// the order, which is a fourth, different fact.
class Diagnosis {
  const Diagnosis({
    required this.validationResult,
    required this.errorMessage,
    required this.outputReadable,
    required this.hypotheses,
    required this.primaryHypothesisIndex,
    required this.recommendedNextAction,
    required this.reasoningSummary,
  });

  final String? validationResult;
  final String? errorMessage;
  final bool outputReadable;
  final List<DiagnosisHypothesis> hypotheses;
  final int? primaryHypothesisIndex;

  /// inspect / repair / replace / monitor, as the agent wrote it.
  final String? recommendedNextAction;
  final String? reasoningSummary;

  bool get failed => validationResult != 'Ok';

  factory Diagnosis.fromJson(Map<String, dynamic> json) {
    return Diagnosis(
      validationResult: json['validationResult'] as String?,
      errorMessage: json['errorMessage'] as String?,
      outputReadable: json['outputReadable'] as bool,
      hypotheses: (json['hypotheses'] as List<dynamic>)
          .map((item) => DiagnosisHypothesis.fromJson(item as Map<String, dynamic>))
          .toList(growable: false),
      primaryHypothesisIndex: json['primaryHypothesisIndex'] as int?,
      recommendedNextAction: json['recommendedNextAction'] as String?,
      reasoningSummary: json['reasoningSummary'] as String?,
    );
  }
}

/// Mirrors the API's `WorkOrderDetailDto` — everything a technician needs before walking
/// in: the machine, the room, when, what to bring and what the agent thinks is wrong.
class WorkOrderDetail {
  const WorkOrderDetail({
    required this.id,
    required this.reportDescription,
    required this.assetId,
    required this.assetTag,
    required this.assetName,
    required this.makeAndModel,
    required this.room,
    required this.assignedTechnicianId,
    required this.assignedTechnicianName,
    required this.status,
    required this.strategy,
    required this.estimatedCost,
    required this.actualCost,
    required this.partsRequired,
    required this.resolutionNote,
    required this.completionPhotoUrl,
    required this.completedAt,
    required this.scheduledSlots,
    required this.diagnosis,
  });

  final int id;

  /// The reporter's own words, rendered verbatim.
  final String reportDescription;

  final int assetId;
  final String assetTag;
  final String assetName;
  final String? makeAndModel;
  final Room room;
  final int? assignedTechnicianId;
  final String? assignedTechnicianName;
  final String status;
  final String strategy;
  final num estimatedCost;
  final num? actualCost;

  /// Free text as the planner wrote it; null when the job needs none.
  final String? partsRequired;
  final String? resolutionNote;
  final String? completionPhotoUrl;
  final String? completedAt;

  /// Oldest booking first, as the API sends it. Empty until a manager books a visit.
  final List<ScheduledSlot> scheduledSlots;

  /// Null when no diagnosis was ever recorded for the report — not the same as a failed one.
  final Diagnosis? diagnosis;

  bool get isCompletable => WorkOrderStatuses.completable.contains(status);

  factory WorkOrderDetail.fromJson(Map<String, dynamic> json) {
    final asset = json['asset'] as Map<String, dynamic>;
    final technician = json['assignedTechnician'] as Map<String, dynamic>?;
    final make = [asset['manufacturer'], asset['model']]
        .whereType<String>()
        .where((part) => part.isNotEmpty);
    final diagnosis = json['diagnosis'] as Map<String, dynamic>?;

    return WorkOrderDetail(
      id: json['id'] as int,
      reportDescription: json['reportDescription'] as String,
      assetId: asset['id'] as int,
      assetTag: asset['assetTag'] as String,
      assetName: asset['name'] as String,
      makeAndModel: make.isEmpty ? null : make.join(' '),
      room: Room.fromJson(json['room'] as Map<String, dynamic>),
      assignedTechnicianId: technician?['id'] as int?,
      assignedTechnicianName: technician?['fullName'] as String?,
      status: json['status'] as String,
      strategy: json['strategy'] as String,
      estimatedCost: json['estimatedCost'] as num,
      actualCost: json['actualCost'] as num?,
      partsRequired: json['partsRequired'] as String?,
      resolutionNote: json['resolutionNote'] as String?,
      completionPhotoUrl: json['completionPhotoUrl'] as String?,
      completedAt: json['completedAt'] as String?,
      scheduledSlots: (json['scheduledSlots'] as List<dynamic>)
          .map((item) => ScheduledSlot.fromJson(item as Map<String, dynamic>))
          .toList(growable: false),
      diagnosis: diagnosis == null ? null : Diagnosis.fromJson(diagnosis),
    );
  }
}
