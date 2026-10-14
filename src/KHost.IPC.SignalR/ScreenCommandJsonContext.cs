using System.Text.Json.Serialization;

namespace KHost.IPC.SignalR;

// The only resolver in SignalR's chain, so every type a hub method takes or returns needs an entry.
// Commands and states cross as string envelopes (see ScreenIpcSerializer), never as payload types.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
internal sealed partial class ScreenCommandJsonContext : JsonSerializerContext { }
