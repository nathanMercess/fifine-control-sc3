using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FifineControl.Core.Logging;

namespace FifineControl.Core.Integrations.Obs;

public sealed class ObsWebSocketService : IObsWebSocketService
{
    private const int HelloOpCode = 0;
    private const int IdentifyOpCode = 1;
    private const int IdentifiedOpCode = 2;
    private const int EventOpCode = 5;
    private const int RequestOpCode = 6;
    private const int RequestResponseOpCode = 7;
    private const int MaximumMessageBytes = 1024 * 1024;

    private readonly IAppLogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private ClientWebSocket? socket;
    private bool disposed;

    public ObsWebSocketService(IAppLogger? logger = null)
    {
        this.logger = logger ?? NullAppLogger.Instance;
    }

    public event EventHandler<ObsConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public ObsConnectionState ConnectionState { get; private set; } = ObsConnectionState.Disconnected;
    public string? ServerVersion { get; private set; }

    public async Task ConnectAsync(
        Uri serverUri,
        string? password,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateServerUri(serverUri);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeSocketAsync(CancellationToken.None).ConfigureAwait(false);
            SetState(ObsConnectionState.Connecting);

            var nextSocket = new ClientWebSocket();
            nextSocket.Options.AddSubProtocol("obswebsocket.json");
            socket = nextSocket;

            try
            {
                await nextSocket.ConnectAsync(serverUri, cancellationToken).ConfigureAwait(false);
                using var hello = await ReceiveJsonAsync(nextSocket, cancellationToken).ConfigureAwait(false);
                var helloRoot = hello.RootElement;
                EnsureOpCode(helloRoot, HelloOpCode, "Hello");

                var helloData = helloRoot.GetProperty("d");
                ServerVersion = GetOptionalString(helloData, "obsWebSocketVersion");
                string? authentication = null;
                if (helloData.TryGetProperty("authentication", out var authenticationData))
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        throw new ObsWebSocketException("O OBS exige senha para esta conexao.");
                    }

                    authentication = ObsAuthentication.CreateResponse(
                        password,
                        authenticationData.GetProperty("salt").GetString() ?? string.Empty,
                        authenticationData.GetProperty("challenge").GetString() ?? string.Empty);
                }

                await SendIdentifyAsync(nextSocket, authentication, cancellationToken).ConfigureAwait(false);
                while (true)
                {
                    using var identified = await ReceiveJsonAsync(nextSocket, cancellationToken).ConfigureAwait(false);
                    var root = identified.RootElement;
                    if (root.GetProperty("op").GetInt32() == IdentifiedOpCode)
                    {
                        break;
                    }
                }

                logger.Info("obs.connected", new { Server = serverUri.ToString(), ServerVersion });
                SetState(ObsConnectionState.Connected, ServerVersion);
            }
            catch (Exception ex)
            {
                logger.Error("obs.connect.failed", ex, new { Server = serverUri.ToString() });
                await DisposeSocketAsync(CancellationToken.None).ConfigureAwait(false);
                SetState(ObsConnectionState.Faulted, errorMessage: ex.Message);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeSocketAsync(cancellationToken).ConfigureAwait(false);
            ServerVersion = null;
            SetState(ObsConnectionState.Disconnected);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ObsRecordingStatus> GetRecordingStatusAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendRequestAsync("GetRecordStatus", cancellationToken).ConfigureAwait(false);
        var data = response.RootElement;
        return new ObsRecordingStatus(
            data.GetProperty("outputActive").GetBoolean(),
            data.GetProperty("outputPaused").GetBoolean(),
            GetOptionalString(data, "outputTimecode"),
            GetOptionalInt64(data, "outputDuration"),
            GetOptionalInt64(data, "outputBytes"));
    }

    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendRequestAsync("StartRecord", cancellationToken).ConfigureAwait(false);
    }

    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendRequestAsync("StopRecord", cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            await DisposeSocketAsync(CancellationToken.None).ConfigureAwait(false);
            ConnectionState = ObsConnectionState.Disconnected;
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private async Task<JsonDocument> SendRequestAsync(
        string requestType,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentSocket = GetConnectedSocket();
            var requestId = Guid.NewGuid().ToString("N");
            await SendJsonAsync(
                currentSocket,
                new
                {
                    op = RequestOpCode,
                    d = new { requestType, requestId }
                },
                cancellationToken).ConfigureAwait(false);

            while (true)
            {
                using var message = await ReceiveJsonAsync(currentSocket, cancellationToken).ConfigureAwait(false);
                var root = message.RootElement;
                var opCode = root.GetProperty("op").GetInt32();
                if (opCode == EventOpCode)
                {
                    continue;
                }

                if (opCode != RequestResponseOpCode)
                {
                    continue;
                }

                var data = root.GetProperty("d");
                if (!string.Equals(data.GetProperty("requestId").GetString(), requestId, StringComparison.Ordinal))
                {
                    continue;
                }

                var requestStatus = data.GetProperty("requestStatus");
                if (!requestStatus.GetProperty("result").GetBoolean())
                {
                    var code = requestStatus.GetProperty("code").GetInt32();
                    var comment = GetOptionalString(requestStatus, "comment") ?? "OBS recusou a solicitacao.";
                    throw new ObsWebSocketException($"{requestType} falhou ({code}): {comment}", code);
                }

                logger.Info("obs.request.succeeded", new { RequestType = requestType });
                return data.TryGetProperty("responseData", out var responseData)
                    ? JsonDocument.Parse(responseData.GetRawText())
                    : JsonDocument.Parse("{}");
            }
        }
        catch (Exception ex) when (ex is WebSocketException or JsonException)
        {
            logger.Error("obs.request.failed", ex, new { RequestType = requestType });
            SetState(ObsConnectionState.Faulted, ServerVersion, ex.Message);
            throw new ObsWebSocketException($"A comunicacao com o OBS falhou durante {requestType}.", ex);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task SendIdentifyAsync(
        ClientWebSocket currentSocket,
        string? authentication,
        CancellationToken cancellationToken)
    {
        object identifyData = authentication is null
            ? new { rpcVersion = 1, eventSubscriptions = 0 }
            : new { rpcVersion = 1, eventSubscriptions = 0, authentication };
        await SendJsonAsync(
            currentSocket,
            new { op = IdentifyOpCode, d = identifyData },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendJsonAsync(
        ClientWebSocket currentSocket,
        object payload,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await currentSocket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            WebSocketMessageFlags.EndOfMessage,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(
        ClientWebSocket currentSocket,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await currentSocket.ReceiveAsync(rented, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new ObsWebSocketException(
                        $"O OBS encerrou a conexao: {result.CloseStatus} {result.CloseStatusDescription}".Trim());
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new ObsWebSocketException("O OBS enviou uma mensagem em formato inesperado.");
                }

                message.Write(rented, 0, result.Count);
                if (message.Length > MaximumMessageBytes)
                {
                    throw new ObsWebSocketException("A mensagem do OBS excedeu o limite de 1 MiB.");
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return JsonDocument.Parse(message.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async Task DisposeSocketAsync(CancellationToken cancellationToken)
    {
        var oldSocket = socket;
        socket = null;
        if (oldSocket is null)
        {
            return;
        }

        try
        {
            if (oldSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await oldSocket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "FifineControl desconectado",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WebSocketException)
        {
            // A conexao ja caiu; liberar o socket ainda e necessario.
        }
        finally
        {
            oldSocket.Dispose();
        }
    }

    private ClientWebSocket GetConnectedSocket()
    {
        if (ConnectionState != ObsConnectionState.Connected || socket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Conecte ao OBS antes de enviar comandos.");
        }

        return socket;
    }

    private void SetState(
        ObsConnectionState state,
        string? serverVersion = null,
        string? errorMessage = null)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(
            this,
            new ObsConnectionStateChangedEventArgs(state, serverVersion, errorMessage));
    }

    private static void ValidateServerUri(Uri serverUri)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        if (!serverUri.IsAbsoluteUri ||
            (serverUri.Scheme != Uri.UriSchemeWs && serverUri.Scheme != Uri.UriSchemeWss))
        {
            throw new ArgumentException("Use um endereco absoluto ws:// ou wss://.", nameof(serverUri));
        }

        if (!string.IsNullOrEmpty(serverUri.UserInfo) ||
            !string.IsNullOrEmpty(serverUri.Query) ||
            !string.IsNullOrEmpty(serverUri.Fragment))
        {
            throw new ArgumentException(
                "O endereco OBS nao pode conter credenciais, query ou fragmento; informe a senha no campo dedicado.",
                nameof(serverUri));
        }
    }

    private static void EnsureOpCode(JsonElement root, int expected, string messageName)
    {
        if (!root.TryGetProperty("op", out var opCode) || opCode.GetInt32() != expected)
        {
            throw new ObsWebSocketException($"O servidor nao enviou a mensagem {messageName} esperada.");
        }
    }

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? GetOptionalInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
}
