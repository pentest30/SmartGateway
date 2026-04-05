using SmartGateway.Core.Interfaces;

namespace SmartGateway.Api.Services;

public class HttpConfigReloadNotifier : IConfigReloadNotifier
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpConfigReloadNotifier> _logger;

    public HttpConfigReloadNotifier(HttpClient httpClient, ILogger<HttpConfigReloadNotifier> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task NotifyConfigChangedAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/_admin/reload", null);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Config reload triggered on gateway host");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify gateway host of config change — manual reload may be needed");
        }
    }
}
