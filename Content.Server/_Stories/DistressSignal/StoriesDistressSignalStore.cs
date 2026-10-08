using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Content.Server._Stories.DistressSignal;

public sealed record StoriesDistressSignalState(
    float MarinesPerXeno,
    List<string> RecentPlanetIds,
    Dictionary<string, int> CarryoverVotes,
    string? SelectedPlanetId);

/// <summary>
/// HTTP boundary to the independent FastAPI/SQLite store. One writer per server key.
/// </summary>
public sealed class StoriesDistressSignalStore : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _serverPath;

    public StoriesDistressSignalStore(string url, string token, string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId) || string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Distress Signal persistence requires a server ID and API token.");

        _client = new HttpClient
        {
            BaseAddress = new Uri(url.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(3),
        };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _serverPath = $"servers/{Uri.EscapeDataString(serverId)}";
    }

    public Task<StoriesDistressSignalState> GetOrCreateState(int recentPlanetCount, float initialMarinesPerXeno) =>
        Send<StoriesDistressSignalState>(HttpMethod.Post, "load", new { recentPlanetCount, initialMarinesPerXeno });

    public Task<List<string>> GetRecentPlanets(int count) =>
        Send<List<string>>(HttpMethod.Post, "history", new { count });

    public Task AddRound(int roundId, string planetId, float marinesPerXeno) =>
        Send<object>(HttpMethod.Put, $"rounds/{roundId}/start", new { planetId, marinesPerXeno });

    public Task<float> FinishRound(int roundId, int result, float marinesPerXeno) =>
        Send<float>(HttpMethod.Put, $"rounds/{roundId}/finish", new { result, marinesPerXeno });

    public Task SetVotingState(string? selectedPlanetId, IReadOnlyDictionary<string, int> carryoverVotes) =>
        Send<object>(HttpMethod.Put, "voting", new { selectedPlanetId, carryoverVotes });

    public Task SetBalance(float marinesPerXeno) =>
        Send<object>(HttpMethod.Put, "balance", new { marinesPerXeno });

    private async Task<T> Send<T>(HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, $"{_serverPath}/{path}")
        {
            Content = JsonContent.Create(body),
        };
        using var response = await _client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty Distress Signal persistence response.");
    }

    public void Dispose() => _client.Dispose();
}
