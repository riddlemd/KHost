using KHost.Plugins.Sdk.Models.QueueRotation;
using KHost.Plugins.Sdk.Services.QueueRotation;

namespace KHost.Abstractions.Services.QueueRotation;

public interface IQueueRotationStrategyFactory
{
    IQueueRotationStrategy Resolve(QueueRotationConfig config);
    IReadOnlyList<IQueueRotationMode> GetAllModes();
}
