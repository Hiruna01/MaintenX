/// The completion form's rules. They mirror `CompleteWorkOrderDto`'s DataAnnotations so a
/// technician hears about a mistake before a round trip; the API checks every one of them
/// again, and its 400 is shown if it disagrees.
library;

/// `ResolutionNote`: `[StringLength(2000, MinimumLength = 20)]`.
///
/// THE FLOOR IS NOT ARBITRARY. This note is copied verbatim into the asset's
/// `ServiceRecord`, and that is what the diagnostic agent reads months later looking for a
/// repeat failure. "done" teaches it nothing — and a note that says nothing is worse than a
/// missing one, because it looks like evidence.
const int minResolutionNoteLength = 20;
const int maxResolutionNoteLength = 2000;

/// `ActualCost`: the decimal `[Range]` "0" to "10000000", in rupees and cents.
const int maxActualCost = 10000000;

/// Rupees with at most two decimal places — no sign, no exponent, no thousands separators.
final RegExp _costPattern = RegExp(r'^\d+(\.\d{1,2})?$');

/// The form's validate(): a message per invalid field, keyed by field name. An empty map
/// means the form may be sent. Same shape as every other form in both clients.
///
/// [outcome] is null until the technician picks one — there is deliberately no default,
/// for the reason `CompleteWorkOrderDto.Outcome` is `[Required]`: a TemporaryFix recorded
/// as Resolved erases exactly the repeat-failure pattern the diagnostic agent reads for.
Map<String, String> validateCompletion({
  required String? outcome,
  required String actualCost,
  required String resolutionNote,
}) {
  final errors = <String, String>{};

  if (outcome == null) {
    errors['outcome'] = 'Choose what the visit came to.';
  }

  final cost = actualCost.trim();
  if (cost.isEmpty) {
    errors['actualCost'] = 'Enter what the job cost — 0 if nothing was spent.';
  } else if (!_costPattern.hasMatch(cost)) {
    errors['actualCost'] = 'Enter an amount in rupees, like 1500 or 1500.50.';
  } else if (num.parse(cost) > maxActualCost) {
    errors['actualCost'] = 'That is more than Rs 10,000,000. Check the amount.';
  }

  // Counted in UTF-16 code units — Dart's String.length, and what the API's StringLength
  // counts — after trimming, so twenty spaces are not a note.
  final note = resolutionNote.trim();
  if (note.length < minResolutionNoteLength) {
    errors['resolutionNote'] =
        'Say what was wrong and what you did — at least $minResolutionNoteLength characters. '
        'This note becomes the asset\'s service history.';
  } else if (note.length > maxResolutionNoteLength) {
    errors['resolutionNote'] = 'Keep it to $maxResolutionNoteLength characters or fewer.';
  }

  return errors;
}

/// The cost as the JSON number the API binds to a C# `decimal`.
///
/// Parsed only once [validateCompletion] has accepted it, so it is at most 8 digits before
/// the point and 2 after — well inside the 15 significant digits a double carries exactly,
/// and Dart writes a double back out as its shortest exact decimal. The text typed is the
/// text sent. Nothing is added, rounded or compared here.
num costForApi(String actualCost) => num.parse(actualCost.trim());
