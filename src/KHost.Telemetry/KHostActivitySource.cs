using System.Diagnostics;

namespace KHost.Telemetry;

public static class KHostActivitySource
{
    public static readonly ActivitySource Source =
        new(KHostTelemetry.ActivitySourceName, KHostTelemetry.Version);
}
