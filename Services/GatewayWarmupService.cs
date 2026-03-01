namespace apiGateWay.Services;
public sealed class GatewayWarmupService : IHostedService
{
    public const string HttpClientName = "GatewayWarmup";
    private const int DefaultTimeoutSeconds = 5;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GatewayWarmupService> _logger;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;
    public GatewayWarmupService(
        IConfiguration configuration,
        ILogger<GatewayWarmupService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        var timeoutSeconds = _configuration.GetValue<int?>("GatewayWarmup:RequestTimeoutSeconds") ?? DefaultTimeoutSeconds;
        _requestTimeout = TimeSpan.FromSeconds(timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds);
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("GatewayWarmup:Enabled", true))
        {
            _logger.LogInformation("Warmup disabled by configuration.");
            return;
        }
        var destinations = GetDestinations();
        if (destinations.Count == 0)
        {
            _logger.LogInformation("Warmup skipped: no destinations configured in ReverseProxy.");
            return;
        }
        _logger.LogInformation("Warmup started for {Count} destinations.", destinations.Count);
        var warmupTasks = destinations.Select(destination => WarmUpDestinationAsync(destination, cancellationToken));
        await Task.WhenAll(warmupTasks);
        _logger.LogInformation("Warmup finished. Gateway ready to proxy requests.");
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private IReadOnlyList<Uri> GetDestinations()
    {
        var destinationSections = _configuration
            .GetSection("ReverseProxy:Clusters")
            .GetChildren()
            .SelectMany(cluster => cluster.GetSection("Destinations").GetChildren());
        var uniqueAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinations = new List<Uri>();
        foreach (var destination in destinationSections)
        {
            var address = destination["Address"];
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                _logger.LogWarning("Warmup ignored invalid address: {Address}", address);
                continue;
            }
            if (uniqueAddresses.Add(uri.AbsoluteUri))
            {
                destinations.Add(uri);
            }
        }
        return destinations;
    }
    private async Task WarmUpDestinationAsync(Uri destination, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_requestTimeout);
        try
        {
            using var response = await _httpClient.GetAsync(destination, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            _logger.LogInformation("Warmup {Destination} responded {StatusCode}.", destination, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Warmup timeout for {Destination} after {TimeoutSeconds}s.", destination, _requestTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup failed for {Destination}.", destination);
        }
    }
}
