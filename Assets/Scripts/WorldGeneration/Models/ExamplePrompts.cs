namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// Shared example prompts — a single source of truth so the Editor test tooling
    /// (WorldGenerationTestTool, Phase 8) and the runtime prompt UI's default text (Phase 9)
    /// never drift apart into two slightly-different copies of the same example.
    /// </summary>
    public static class ExamplePrompts
    {
        /// <summary>Exercises terrain, environment, obstacles, course style/gate count/sections, spawn, and lighting/weather all at once — the standing example throughout this project's docs.</summary>
        public const string Himalayan =
            "Create a cinematic Himalayan FPV racing course with steep mountains, pine forests, " +
            "waterfalls, cliffs, abandoned cabins, narrow tunnels and 15 racing gates. Make the " +
            "first section technical and tight, then transition into a high-speed valley section.";
    }
}
