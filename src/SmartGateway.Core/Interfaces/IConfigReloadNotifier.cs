namespace SmartGateway.Core.Interfaces;

public interface IConfigReloadNotifier
{
    Task NotifyConfigChangedAsync();
}
