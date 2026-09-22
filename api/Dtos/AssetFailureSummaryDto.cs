using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Dtos;

/// <summary>
/// What an asset's service history adds up to, as of today. This is the asset registry's
/// one business operation rather than another CRUD read.
///
/// EVERY FIGURE HERE IS A COUNT OR A DATE COMPARISON PERFORMED IN C#. Nothing on this
/// record is an opinion, an estimate, or anything a model produced — whether a machine is
/// a repeat failure is a deterministic business rule, and deterministic business rules
/// live in C#. The diagnostic agent may read these numbers through a tool call; it never
/// computes them, and neither does a prompt.
/// </summary>
public record AssetFailureSummaryDto(
    int AssetId,

    string AssetTag,

    // Service visits in the last 12 months.
    int FailureCount12Months,

    // Service visits in the last 90 days. "Three months" means exactly 90 days here, so
    // this number and IsRepeatFailure read the SAME window and cannot disagree — a summary
    // saying two visits this quarter beside a repeat-failure flag would be read as a bug,
    // and would be one.
    int FailureCount3Months,

    // The most recent visit, or null when the asset has never been serviced.
    DateOnly? LastServicedOn,

    // Days since that visit, or null when there has not been one. Null is not zero: a
    // machine nobody has ever touched is not a machine serviced today.
    int? DaysSinceLastService,

    // Visits across the WHOLE history that ended in a TemporaryFix, not just recent ones.
    // A run of temporary fixes is the shape of a fault that keeps coming back, and the
    // first one is part of that shape.
    int TemporaryFixCount,

    // True when a warranty is recorded and has not expired. A null WarrantyExpiresOn means
    // "no warranty recorded", which is not the same fact as "expired" — but it is not
    // cover either, so it reads false here.
    bool IsUnderWarranty,

    // Three or more service visits in the last 90 days. The threshold is a number in C#,
    // deliberately, so the same history gives the same answer every time it is asked.
    bool IsRepeatFailure,

    // Which outcomes appear in the history at all, deduplicated, in enum order. Serialised
    // by NAME like every other enum here, so a client never matches on an ordinal.
    IReadOnlyList<ServiceOutcome> DistinctOutcomes);
