namespace Sim.WorldGeneration.Persistence
{
    /// <summary>Turns a WorldSaveData into save-file text and back. Its own small interface (mirroring IWorldSpecificationJsonParser) so WorldSaveService is testable without touching a real serializer, and so the actual JSON settings live in exactly one place.</summary>
    public interface IWorldSaveSerializer
    {
        string Serialize(WorldSaveData data);

        WorldSaveDeserializeResult Deserialize(string json);
    }
}
