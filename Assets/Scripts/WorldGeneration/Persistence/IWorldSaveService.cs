namespace Sim.WorldGeneration.Persistence
{
    /// <summary>
    /// Responsible for persistence only: writing/reading/deleting a WorldSaveData. Does NOT
    /// generate worlds, call an LLM, manipulate drone physics, own course gameplay, own UI, or
    /// create any Unity GameObject — WorldGenerationRuntimeService is the one place that bridges
    /// this to the actual generation pipeline (Sim.Core.WorldGenerationController.LoadWorld).
    ///
    /// <paramref name="slotName"/> everywhere below is optional — omit it (or pass null/empty)
    /// to use the single default slot, which is all the current UI ever needs
    /// ("keep it simple," per this phase's explicit instruction). A non-default value is still
    /// validated against a strict allow-list (see WorldSaveService) so this interface can never
    /// be used to write outside the controlled save directory.
    /// </summary>
    public interface IWorldSaveService
    {
        WorldSaveOperationResult Save(WorldSaveData data, string slotName = null);

        /// <summary>Reads, deserializes, AND validates (including the full WorldSpecificationValidator pass) before returning — a caller receiving Success can hand Data.Specification straight to WorldGenerationController.LoadWorld with no further checking.</summary>
        WorldLoadResult Load(string slotName = null);

        bool Delete(string slotName = null);

        bool Exists(string slotName = null);
    }
}
