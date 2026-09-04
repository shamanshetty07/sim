namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// Result of one IHttpTransport.PostJsonAsync call that actually completed (cancellation is
    /// signalled via OperationCanceledException, never through this type — see IHttpTransport).
    /// Two distinct failure shapes, mirroring UnityWebRequest's own split (see
    /// OpenWorldReactorWorldGenerationService.MintSessionTokenAsync, the established pattern
    /// this mirrors): a connection-level failure (DNS, no network, refused) never reached the
    /// server at all, vs. a completed HTTP exchange that happened to return a non-2xx status —
    /// callers generally want to treat both as "provider unavailable" but the distinction is
    /// preserved here in case a caller wants to log or handle them differently.
    /// </summary>
    public sealed class HttpTransportResponse
    {
        public bool IsConnectionError { get; private set; }
        public string ConnectionErrorMessage { get; private set; }

        public long StatusCode { get; private set; }
        public string Body { get; private set; }

        public bool IsSuccessStatusCode => !IsConnectionError && StatusCode >= 200 && StatusCode <= 299;

        private HttpTransportResponse() { }

        public static HttpTransportResponse ConnectionError(string message) => new HttpTransportResponse
        {
            IsConnectionError = true,
            ConnectionErrorMessage = message
        };

        public static HttpTransportResponse Completed(long statusCode, string body) => new HttpTransportResponse
        {
            IsConnectionError = false,
            StatusCode = statusCode,
            Body = body
        };
    }
}
