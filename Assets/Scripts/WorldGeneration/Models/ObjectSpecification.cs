namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// One category of environment object to place — trees, rocks, buildings, waterfalls,
    /// bridges, tunnels, abandoned buildings, whatever the prompt implies. <see cref="Category"/>
    /// is a free-form string rather than a fixed enum of {Tree, Rock, Building}, specifically
    /// so a prompt like "abandoned buildings and neon lighting" isn't forced to collapse onto
    /// whatever fixed categories were decided on before OpenWorld Reactor existed. Phase 9's
    /// PrefabRegistry falls back to a primitive placeholder for any category it doesn't
    /// recognize (per the project's asset-placement rule) rather than rejecting it.
    /// </summary>
    public sealed class ObjectSpecification
    {
        /// <summary>Free-form: "tree", "rock", "building", "waterfall", "bridge", "tunnel", "abandoned_building", etc.</summary>
        public string Category { get; set; }

        /// <summary>Absolute count, when the prompt/backend implies a specific number. Mutually usable alongside Density01 — a generator may use whichever is more convenient for a given category.</summary>
        public int Count { get; set; }

        /// <summary>0-1 alternative to Count for area-based placement (e.g. "dense forest" implying a coverage fraction rather than a tree count).</summary>
        public float Density01 { get; set; }

        /// <summary>Free-form placement hint: "along_cliffs", "riverbank", "scattered", "dense_cluster", etc. Optional — empty means "generator's default placement for this category".</summary>
        public string PlacementHint { get; set; }
    }
}
