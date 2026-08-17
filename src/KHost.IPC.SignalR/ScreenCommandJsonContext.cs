using KHost.Abstractions.Services.IPC;
using System.Text.Json.Serialization;

namespace KHost.IPC.SignalR;

[JsonSerializable(typeof(ScreenCommandBase))]
[JsonSerializable(typeof(LoadMediaCommand))]
[JsonSerializable(typeof(PlayCommand))]
[JsonSerializable(typeof(PauseCommand))]
[JsonSerializable(typeof(StopCommand))]
[JsonSerializable(typeof(SeekCommand))]
[JsonSerializable(typeof(SetVolumeCommand))]
[JsonSerializable(typeof(SetPitchCommand))]
[JsonSerializable(typeof(SetTimelineCommand))]
[JsonSerializable(typeof(ScreenStateBase))]
[JsonSerializable(typeof(ScreenPlaybackState))]
// This context is the only resolver in SignalR's chain, so a hub method's own argument and return
// types need entries too — they are not covered unless some command property already uses them.
// ScreenHub.EchoClock returns this one; without it the hub aborts the connection mid-call.
[JsonSerializable(typeof(long))]
internal sealed partial class ScreenCommandJsonContext : JsonSerializerContext { }
