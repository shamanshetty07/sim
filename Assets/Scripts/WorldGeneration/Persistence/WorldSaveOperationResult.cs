namespace Sim.WorldGeneration.Persistence
{
    /// <summary>Result of one WorldSaveService.Save call.</summary>
    public sealed class WorldSaveOperationResult
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }

        private WorldSaveOperationResult() { }

        public static WorldSaveOperationResult Succeeded() => new WorldSaveOperationResult { Success = true };

        public static WorldSaveOperationResult Failed(string message) =>
            new WorldSaveOperationResult { Success = false, ErrorMessage = message };
    }
}
