namespace apiGateWay.Services;

public sealed class GatewayWarmupService : IHostedService
{
    public const string HttpClientName = "GatewayWarmup";

    private const int DefaultTimeoutSeconds = 30;
    private const int DefaultMaxRetries = 5;
    private const int DefaultRetryDelaySeconds = 4;
    private const string DefaultWarmupPath = "/";

    private readonly IConfiguration _configuration;
    private readonly ILogger<GatewayWarmupService> _logger;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private readonly string _defaultWarmupPath;

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

        var maxRetries = _configuration.GetValue<int?>("GatewayWarmup:MaxRetries") ?? DefaultMaxRetries;
        _maxRetries = maxRetries < 0 ? DefaultMaxRetries : maxRetries;

        var retryDelaySeconds = _configuration.GetValue<int?>("GatewayWarmup:RetryDelaySeconds") ?? DefaultRetryDelaySeconds;
        _retryDelay = TimeSpan.FromSeconds(retryDelaySeconds < 0 ? DefaultRetryDelaySeconds : retryDelaySeconds);

        _defaultWarmupPath = _configuration["GatewayWarmup:DefaultPath"] ?? DefaultWarmupPath;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("GatewayWarmup:Enabled", true))
        {
            _logger.LogInformation("Warmup disabled by configuration.");
            return;
        }

        var warmupTargets = GetWarmupTargets();
        if (warmupTargets.Count == 0)
        {
            _logger.LogInformation("Warmup skipped: no valid destinations configured in ReverseProxy.");
            return;
        }

        _logger.LogInformation(
            "Warmup started for {Count} targets. Timeout={TimeoutSeconds}s Retries={Retries} Delay={DelaySeconds}s",
            warmupTargets.Count,
            _requestTimeout.TotalSeconds,
            _maxRetries,
            _retryDelay.TotalSeconds);

        var warmupTasks = warmupTargets.Select(target => WarmUpTargetAsync(target, cancellationToken));
        await Task.WhenAll(warmupTasks);

        _logger.LogInformation("Warmup finished. Gateway ready to proxy requests.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private IReadOnlyList<WarmupTarget> GetWarmupTargets()
    {
        var clusters = _configuration
            .GetSection("ReverseProxy:Clusters")
            .GetChildren();

        var uniqueAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<WarmupTarget>();

        foreach (var cluster in clusters)
        {
            var clusterName = cluster.Key;
            var clusterWarmupPath = cluster["WarmupPath"];
            var warmupPath = string.IsNullOrWhiteSpace(clusterWarmupPath) ? _defaultWarmupPath : clusterWarmupPath!;

            foreach (var destination in cluster.GetSection("Destinations").GetChildren())
            {
                var address = destination["Address"];
                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                if (!Uri.TryCreate(address, UriKind.Absolute, out var baseUri))
                {
                    _logger.LogWarning("Warmup ignored invalid address for cluster {Cluster}: {Address}", clusterName, address);
                    continue;
                }

                var warmupUri = BuildWarmupUri(baseUri, warmupPath);
                var uniqueKey = $"{clusterName}|{warmupUri.AbsoluteUri}";

                if (uniqueAddresses.Add(uniqueKey))
                {
                    targets.Add(new WarmupTarget(clusterName, warmupUri));
                }
            }
        }

        return targets;
    }

    private static Uri BuildWarmupUri(Uri baseUri, string warmupPath)
    {
        if (string.IsNullOrWhiteSpace(warmupPath))
        {
            return baseUri;
        }

        if (warmupPath.StartsWith('/'))
        {
            var host = $"{baseUri.Scheme}://{baseUri.Authority}";
            return new Uri($"{host}{warmupPath}");
        }

        return new Uri(baseUri, warmupPath);
    }

    private async Task WarmUpTargetAsync(WarmupTarget target, CancellationToken cancellationToken)
    {
        var totalAttempts = _maxRetries + 1;

        for (var attempt = 1; attempt <= totalAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_requestTimeout);

            try
            {
                using var response = await _httpClient.GetAsync(
                    target.Uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                var statusCode = (int)response.StatusCode;
                if (statusCode < 500)
                {
                    _logger.LogInformation(
                        "Warmup success for cluster {Cluster} ({Uri}) with status {StatusCode} on attempt {Attempt}/{TotalAttempts}.",
                        target.ClusterName,
                        target.Uri,
                        statusCode,
                        attempt,
                        totalAttempts);
                    return;
                }

                _logger.LogWarning(
                    "Warmup received status {StatusCode} for cluster {Cluster} ({Uri}) on attempt {Attempt}/{TotalAttempts}.",
                    statusCode,
                    target.ClusterName,
                    target.Uri,
                    attempt,
                    totalAttempts);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Warmup timeout for cluster {Cluster} ({Uri}) on attempt {Attempt}/{TotalAttempts} after {TimeoutSeconds}s.",
                    target.ClusterName,
                    target.Uri,
                    attempt,
                    totalAttempts,
                    _requestTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Warmup failed for cluster {Cluster} ({Uri}) on attempt {Attempt}/{TotalAttempts}.",
                    target.ClusterName,
                    target.Uri,
                    attempt,
                    totalAttempts);
            }

            if (attempt < totalAttempts && _retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(_retryDelay, cancellationToken);
            }
        }

        _logger.LogError(
            "Warmup exhausted all attempts for cluster {Cluster} ({Uri}).",
            target.ClusterName,
            target.Uri);
    }

    private sealed record WarmupTarget(string ClusterName, Uri Uri);
}
