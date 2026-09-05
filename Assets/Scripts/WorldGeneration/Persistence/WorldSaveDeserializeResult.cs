namespace Sim.WorldGeneration.Persistence
{
    /// <summary>Result of attempting to deserialize save-file text into a WorldSaveData. Failure here means "not usable JSON for our schema" — an expected outcome for untrusted on-disk input, not an exceptional one. Same Succeeded/Failed pattern as WorldSpecificationParseResult/SpawnResolutionResult elsewhere in this project.</summary>
    public sealed class WorldSaveDeserializeResult
    {
        public bool Success { get; private set; }
        public WorldSaveData Data { get; private set; }
        public string ErrorMessage { get; private set; }

        private WorldSaveDeserializeResult() { }

        public static WorldSaveDeserializeResult Succeeded(WorldSaveData data) =>
            new WorldSaveDeserializeResult { Success = true, Data = data };

        public static WorldSaveDeserializeResult Failed(string message) =>
            new WorldSaveDeserializeResult { Success = false, ErrorMessage = message };
    }
}
