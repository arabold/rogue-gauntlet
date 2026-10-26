using Godot;

/// <summary>
/// A floor's special encounters, decided once by its mob factory before generation places anything. Generation resolves
/// the gatekeeper site and applies the nest and gatekeeper scenes to spawn points; the level's encounter controller only
/// reads it for the descent objective. <see cref="GatekeeperId"/> marks a gatekeeper floor even when its scene is
/// missing, so a misconfigured floor keeps its descent sealed instead of opening it.
/// </summary>
public sealed record EncounterPlan(RealmEncounterFactory Definition, PackedScene Nest, PackedScene Gatekeeper,
	LargeActorFitProfile GatekeeperFit, string GatekeeperId);