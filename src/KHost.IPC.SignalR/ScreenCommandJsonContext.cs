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
[JsonSerializable(typeof(SetTimelineCommand))]
[JsonSerializable(typeof(SetVideoCommand))]
[JsonSerializable(typeof(LoadBackgroundCommand))]
[JsonSerializable(typeof(PlayBackgroundCommand))]
[JsonSerializable(typeof(PauseBackgroundCommand))]
[JsonSerializable(typeof(StopBackgroundCommand))]
[JsonSerializable(typeof(SetBackgroundVolumeCommand))]
[JsonSerializable(typeof(ShowImageCommand))]
[JsonSerializable(typeof(HideImageCommand))]
[JsonSerializable(typeof(ScreenStateBase))]
[JsonSerializable(typeof(ScreenPlaybackState))]
[JsonSerializable(typeof(ScreenBackgroundState))]
// The only resolver in SignalR's chain, so a hub method's own argument and return types need
// entries too. ScreenHub.EchoClock returns this one.
[JsonSerializable(typeof(long))]
internal sealed partial class ScreenCommandJsonContext : JsonSerializerContext { }
