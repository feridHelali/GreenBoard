using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using InfiniteCanvas.Core.Models;

namespace InfiniteCanvas.App;

/// <summary>
/// Écoute le canal nommé local <see cref="BoardProtocol.PipeName"/> (utilisateur courant uniquement)
/// et exécute les commandes du serveur MCP sur le thread de l'interface.
/// </summary>
public sealed class BoardPipeServer(Dispatcher dispatcher, Func<BoardRequest, BoardResponse> handler) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public void Start() => _ = Task.Run(() => LoopAsync(_cts.Token));

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    BoardProtocol.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(ct);
                await HandleConnectionAsync(pipe, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Un client défaillant ne doit jamais faire tomber l'application
                Debug.WriteLine($"[GreenBoard.Pipe] {ex}");
                await Task.Delay(200, CancellationToken.None);
            }
        }
    }

    private async Task HandleConnectionAsync(Stream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        string? line = await reader.ReadLineAsync(ct);
        if (line == null) return;

        BoardResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<BoardRequest>(line, BoardProtocol.Json)
                          ?? throw new InvalidDataException("Requête vide.");
            response = await dispatcher.InvokeAsync(() => handler(request));
        }
        catch (Exception ex)
        {
            response = BoardResponse.Fail(ex.Message);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, BoardProtocol.Json));
    }

    public void Dispose() => _cts.Cancel();
}
