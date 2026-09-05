namespace Sim.WorldGeneration.Persistence
{
    /// <summary>Result of one WorldSaveService.Load call — success means Data.Specification has already passed WorldSaveValidator (including the existing WorldSpecificationValidator), so it is immediately safe to hand to WorldGenerationController.LoadWorld.</summary>
    public sealed class WorldLoadResult
    {
        public bool Success { get; private set; }
        public WorldSaveData Data { get; private set; }
        public string ErrorMessage { get; private set; }

        private WorldLoadResult() { }

        public static WorldLoadResult Succeeded(WorldSaveData data) => new WorldLoadResult { Success = true, Data = data };

        public static WorldLoadResult Failed(string message) => new WorldLoadResult { Success = false, ErrorMessage = message };
    }
}
