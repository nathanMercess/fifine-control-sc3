using System.Text.Json.Serialization;

namespace FifineControl.Core.Configuration;

public sealed record ObsWebSocketSettings
{
    public string ServerUri { get; init; } = "ws://127.0.0.1:4455";

    // Authentication secrets are session-only. They must never be written to settings.json.
    [JsonIgnore]
    public string Password { get; init; } = string.Empty;
    public bool ConnectOnStartup { get; init; }
}
