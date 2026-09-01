using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services.QueueRotation;

namespace KHost.Abstractions.Services.QueueRotation;

public interface IQueueRotationStrategyFactory
{
    IQueueRotationStrategy Resolve(QueueRotationConfig config);
    IReadOnlyList<IQueueRotationMode> GetAllModes();
}
