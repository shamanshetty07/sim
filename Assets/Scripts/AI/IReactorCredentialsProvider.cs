namespace Sim.AI
{
    /// <summary>
    /// Abstraction over where OpenWorld Reactor credentials come from. Exists as its own
    /// interface (rather than a static lookup baked into the service) specifically so tests
    /// can inject a deterministic "no credentials" or fake-credentials provider regardless of
    /// what actually happens to be present in the real environment (env vars, a local
    /// .env.local file) on whatever machine the tests run on — see
    /// EnvironmentReactorCredentialsProviderTests / OpenWorldReactorWorldGenerationServiceTests
    /// for why this matters: a developer machine with real credentials configured must not
    /// cause automated tests to make live network calls.
    /// </summary>
    public interface IReactorCredentialsProvider
    {
        bool TryGetApiKey(out string apiKey);
        bool TryGetModel(out string model);
    }
}
