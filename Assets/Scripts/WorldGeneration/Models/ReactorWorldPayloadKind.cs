namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// What kind of payload a ReactorWorldResult is actually carrying. Exists because
    /// OpenWorld Reactor's real output shape is unknown (see docs/WORLD_SPECIFICATION.md
    /// "Open questions") — it may return a structured description of a world, a reference to
    /// an actual scene/asset representation it generated directly, both, or (until real
    /// integration exists) neither.
    /// </summary>
    public enum ReactorWorldPayloadKind
    {
        /// <summary>No payload beyond the normalized fields on ReactorWorldResult itself (name/description/seed/metadata).</summary>
        Unknown,

        /// <summary>StructuredPayloadJson holds backend-native structured data describing the world.</summary>
        StructuredData,

        /// <summary>NativeAssetReference holds a handle/URI/identifier to a scene or asset representation the backend generated directly, not yet a Unity integration mechanism this project has implemented.</summary>
        NativeSceneReference
    }
}
