using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NotifyIsland.Core;
using NotifyIsland.Platform;

namespace NotifyIsland.Integration.Windows;

internal sealed class NamedPipeIpc : IIpcEndpoint
{
    public const string PipeName = "NotifyIsland";
    public string Name => @"\\.\pipe\" + PipeName;
    public string Status { get; private set; } = "Off";
    public event Action<NotificationRequest>? NotifyReceived;
    public event Action<NotificationId>? DismissReceived;

    private CancellationTokenSource? _cts;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        Status = "Listening " + Name;
        IslandLog.Write("ipc", Status);
        _ = Task.Run(() => Loop(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        Status = "Off";
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                HandleLine(line);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Status = "Error " + ex.GetType().Name;
                IslandLog.Write("ipc", Status + " " + ex.Message);
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
        }
    }

    private void HandleLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var op = root.TryGetProperty("op", out var opEl) ? opEl.GetString() : "notify";
            if (string.Equals(op, "dismiss", StringComparison.OrdinalIgnoreCase))
            {
                var id = NotificationId.Parse(root.TryGetProperty("id", out var idEl) ? idEl.GetString() : "");
                DismissReceived?.Invoke(id);
                return;
            }
            var req = new NotificationRequest
            {
                Id = NotificationId.Parse(root.TryGetProperty("id", out var nid) ? nid.GetString() : ""),
                Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                Template = root.TryGetProperty("template", out var tpl) ? tpl.GetString() ?? "notify" : "notify",
                AppId = root.TryGetProperty("appId", out var app) ? app.GetString() ?? "" : "",
                DurationMs = root.TryGetProperty("durationMs", out var d) && d.TryGetInt32(out var ms) ? ms : 4000,
                Replace = !string.Equals(op, "notify", StringComparison.OrdinalIgnoreCase) || true
            };
            NotifyReceived?.Invoke(req);
        }
        catch (Exception ex)
        {
            IslandLog.Write("ipc", "bad json " + ex.GetType().Name);
        }
    }
}
