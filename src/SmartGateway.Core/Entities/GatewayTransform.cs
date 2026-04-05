namespace SmartGateway.Core.Entities;

public class GatewayTransform
{
    public int Id { get; set; }
    public string RouteId { get; set; } = default!;
    public string Type { get; set; } = default!;  // RequestHeader, ResponseHeader, PathPrefix
    public string Key { get; set; } = default!;    // Header name or transform key
    public string? Value { get; set; }              // Header value, prefix, etc.
    public string Action { get; set; } = "Set";    // Set, Append, Remove

    public GatewayRoute? Route { get; set; }
}
