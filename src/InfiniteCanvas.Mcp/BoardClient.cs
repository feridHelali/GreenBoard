using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using InfiniteCanvas.Core.Models;

namespace InfiniteCanvas.Mcp;

/// <summary>Client du canal nommé local exposé par l'application GreenBoard ouverte.</summary>
public sealed class BoardClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    public async Task<BoardResponse> SendAsync(BoardRequest request, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(
            ".", BoardProtocol.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            await pipe.ConnectAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BoardUnavailableException();
        }

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, BoardProtocol.Json).AsMemory(), ct);
        string? line = await reader.ReadLineAsync(ct);
        return line == null
            ? BoardResponse.Fail("GreenBoard a fermé la connexion sans répondre.")
            : JsonSerializer.Deserialize<BoardResponse>(line, BoardProtocol.Json) ?? BoardResponse.Fail("Réponse vide.");
    }
}

public sealed class BoardUnavailableException()
    : Exception("GreenBoard n'est pas ouvert. Demandez à l'utilisateur de lancer l'application GreenBoard, puis réessayez.");
