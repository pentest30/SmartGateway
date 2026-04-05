namespace SmartGateway.Core.Interfaces;

public interface IDestinationWeightProvider
{
    int GetWeight(string clusterId, string destinationId);
}
