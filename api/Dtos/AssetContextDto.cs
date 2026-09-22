namespace CampusFacilities.Api.Dtos;

/// <summary>
/// An asset plus the two names that make it possible to talk about it: what kind of thing
/// it is and where it is. This is what the `get_asset` agent tool returns.
///
/// Composed of <see cref="AssetDto"/> rather than repeating its fields, so the tool's
/// payload cannot drift away from the one the REST endpoints return.
///
/// It carries the NAMES, not the category and room DTOs, and no service history at all —
/// unlike <see cref="AssetDetailDto"/>. A tool reply is read by a model with a context
/// window: the ids are already on the asset, the timestamps on a room are noise, and the
/// history is a separate tool with a cap on it precisely because it is the part that
/// grows without limit.
/// </summary>
public record AssetContextDto(
    AssetDto Asset,
    string CategoryName,
    string RoomName);
