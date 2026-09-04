using System.Collections.Generic;

namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// Unity's own normalized, bounded description of what to build — the *only* input type
    /// WorldGenerator (Phase 7+) accepts, always after validation.
    ///
    /// This is deliberately NOT a claim about what OpenWorld Reactor natively generates.
    /// Earlier design assumed the AI layer's job was to hand back something that already
    /// looked like this. That's now explicit as false: OpenWorld Reactor's real output is
    /// unknown (see docs/WORLD_SPECIFICATION.md), and ReactorWorldAdapter is the one place
    /// that translates whatever it does return into this shape. If Reactor turns out to
    /// generate richer content than this can express, the answer is to extend this type or
    /// carry a native reference alongside it (see ReactorWorldResult.NativeAssetReference) —
    /// not to force everything through a smaller, lossy model just because it existed first.
    ///
    /// <see cref="OriginalPrompt"/> is carried on every instance specifically so the prompt
    /// that produced a world is never lost between generation and save/load — it is not
    /// reduced to a biome string or discarded once terrain/environment fields are populated.
    /// </summary>
    public sealed class WorldSpecification
    {
        /// <summary>The user's complete original prompt. Never derived-away — see class remarks.</summary>
        public string OriginalPrompt { get; set; } = string.Empty;

        public string WorldName { get; set; } = "Generated World";
        public string Description { get; set; } = string.Empty;

        public int Seed { get; set; }
        public WorldScale Scale { get; set; } = WorldScale.Medium;

        /// <summary>What the prompt implies about how the world should fly — see class remarks on FlightCharacteristics for why this is separate from Terrain.</summary>
        public FlightCharacteristics Flight { get; set; } = new FlightCharacteristics();

        public TerrainSpecification Terrain { get; set; } = new TerrainSpecification();

        /// <summary>Open-ended list of environment object categories to place — see ObjectSpecification remarks.</summary>
        public List<ObjectSpecification> EnvironmentObjects { get; set; } = new List<ObjectSpecification>();

        public List<ObstacleSpecification> Obstacles { get; set; } = new List<ObstacleSpecification>();

        public WeatherSpecification Weather { get; set; } = new WeatherSpecification();
        public LightingSpecification Lighting { get; set; } = new LightingSpecification();
        public SpawnSpecification Spawn { get; set; } = new SpawnSpecification();

        /// <summary>Provenance — which backend, which request, when. Carried through to Persistence (Phase 12/13).</summary>
        public WorldGenerationMetadata Metadata { get; set; }
    }
}
