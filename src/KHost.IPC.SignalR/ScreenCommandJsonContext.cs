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
[JsonSerializable(typeof(ScreenStateBase))]
[JsonSerializable(typeof(ScreenPlaybackState))]
internal sealed partial class ScreenCommandJsonContext : JsonSerializerContext { }
