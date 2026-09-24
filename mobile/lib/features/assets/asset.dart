import '../reports/room.dart';

/// Mirrors the API's `AssetStatus` enum. Matched by NAME, never by ordinal — these are the
/// exact strings the API sends, so a member inserted into the C# enum cannot shift meaning.
class AssetStatuses {
  const AssetStatuses._();

  static const String active = 'Active';
  static const String underMaintenance = 'UnderMaintenance';
  static const String retired = 'Retired';

  static String label(String status) => splitPascalCase(status);
}

/// Mirrors the API's `ServiceOutcome` enum, by NAME.
class ServiceOutcomes {
  const ServiceOutcomes._();

  static const String resolved = 'Resolved';
  static const String temporaryFix = 'TemporaryFix';
  static const String partReplaced = 'PartReplaced';
  static const String noFaultFound = 'NoFaultFound';

  /// Every member, for the completion form's outcome picker.
  static const List<String> all = [resolved, temporaryFix, partReplaced, noFaultFound];

  static String label(String outcome) => splitPascalCase(outcome);
}

/// "UnderMaintenance" -> "Under Maintenance".
String splitPascalCase(String value) =>
    value.replaceAllMapped(RegExp(r'([a-z])([A-Z])'), (m) => '${m[1]} ${m[2]}');

const List<String> _months = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
];

/// Formats an API `DateOnly` ("2026-07-03") as "3 Jul 2026".
///
/// The string is read as its three parts and never converted through a timezone: the API
/// made these `DateOnly` precisely so a calendar date cannot move across midnight.
String formatDateOnly(String? value) {
  final match = RegExp(r'^(\d{4})-(\d{2})-(\d{2})$').firstMatch(value ?? '');
  if (match == null) return '—';
  final month = int.parse(match[2]!);
  return '${int.parse(match[3]!)} ${_months[month - 1]} ${match[1]}';
}

/// One visit from the asset's service history. Immutable once written — see ServiceRecord
/// in CLAUDE.md, which is deliberately not a WorkOrder.
class ServiceRecord {
  const ServiceRecord({
    required this.id,
    required this.servicedOn,
    required this.technicianName,
    required this.technicianNote,
    required this.outcome,
    required this.workOrderId,
  });

  final int id;

  /// A `DateOnly` string, "YYYY-MM-DD". Format it with [formatDateOnly].
  final String servicedOn;
  final String technicianName;

  /// Exactly as the technician wrote it. Rendered verbatim, never truncated.
  final String? technicianNote;
  final String outcome;

  /// Null for seeded and imported history, which no work order produced.
  final int? workOrderId;

  factory ServiceRecord.fromJson(Map<String, dynamic> json) {
    return ServiceRecord(
      id: json['id'] as int,
      servicedOn: json['servicedOn'] as String,
      technicianName: json['technicianName'] as String,
      technicianNote: json['technicianNote'] as String?,
      outcome: json['outcome'] as String,
      workOrderId: json['workOrderId'] as int?,
    );
  }
}

/// Mirrors the API's AssetDetailDto: the asset, its category and room resolved, and its
/// service history OLDEST FIRST — the order a repeat failure reads as one.
class AssetDetail {
  const AssetDetail({
    required this.id,
    required this.assetTag,
    required this.name,
    required this.categoryName,
    required this.room,
    required this.manufacturer,
    required this.model,
    required this.installedOn,
    required this.warrantyExpiresOn,
    required this.status,
    required this.serviceHistory,
  });

  final int id;

  /// The QR payload. Unique across the estate, and never editable.
  final String assetTag;
  final String name;
  final String categoryName;
  final Room room;
  final String? manufacturer;
  final String? model;
  final String installedOn;

  /// Null means "no warranty recorded", which is not the same fact as "expired".
  final String? warrantyExpiresOn;
  final String status;
  final List<ServiceRecord> serviceHistory;

  /// "Epson EB-L630U", or null when neither is recorded.
  String? get makeAndModel {
    final parts = [manufacturer, model].whereType<String>().where((s) => s.isNotEmpty);
    return parts.isEmpty ? null : parts.join(' ');
  }

  factory AssetDetail.fromJson(Map<String, dynamic> json) {
    return AssetDetail(
      id: json['id'] as int,
      assetTag: json['assetTag'] as String,
      name: json['name'] as String,
      categoryName: (json['category'] as Map<String, dynamic>)['name'] as String,
      room: Room.fromJson(json['room'] as Map<String, dynamic>),
      manufacturer: json['manufacturer'] as String?,
      model: json['model'] as String?,
      installedOn: json['installedOn'] as String,
      warrantyExpiresOn: json['warrantyExpiresOn'] as String?,
      status: json['status'] as String,
      // Kept in the order the API sent it. Nothing on the client re-sorts the history.
      serviceHistory: (json['serviceHistory'] as List<dynamic>)
          .map((item) => ServiceRecord.fromJson(item as Map<String, dynamic>))
          .toList(growable: false),
    );
  }
}

/// Mirrors the API's AssetFailureSummaryDto.
///
/// Every field is a count or a date comparison computed by the API in C#. The app displays
/// them and recomputes none — failure counts and warranty dates are deterministic business
/// rules, and a second copy of a rule in Dart is a second answer waiting to disagree.
class FailureSummary {
  const FailureSummary({
    required this.failureCount12Months,
    required this.failureCount3Months,
    required this.lastServicedOn,
    required this.daysSinceLastService,
    required this.temporaryFixCount,
    required this.isUnderWarranty,
    required this.isRepeatFailure,
  });

  final int failureCount12Months;

  /// "Three months" is exactly 90 days on the API side, the same window as [isRepeatFailure].
  final int failureCount3Months;

  /// Null is not zero: a machine nobody has touched is not a machine serviced today.
  final String? lastServicedOn;
  final int? daysSinceLastService;
  final int temporaryFixCount;
  final bool isUnderWarranty;
  final bool isRepeatFailure;

  factory FailureSummary.fromJson(Map<String, dynamic> json) {
    return FailureSummary(
      failureCount12Months: json['failureCount12Months'] as int,
      failureCount3Months: json['failureCount3Months'] as int,
      lastServicedOn: json['lastServicedOn'] as String?,
      daysSinceLastService: json['daysSinceLastService'] as int?,
      temporaryFixCount: json['temporaryFixCount'] as int,
      isUnderWarranty: json['isUnderWarranty'] as bool,
      isRepeatFailure: json['isRepeatFailure'] as bool,
    );
  }
}
