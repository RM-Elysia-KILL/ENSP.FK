using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ENSP.FK.Services;

/// <summary>
/// Lightweight async Telnet client for connecting to eNSP devices.
/// Handles basic Telnet negotiation by rejecting all options.
/// </summary>
public class TelnetService : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private volatile bool _disconnecting;

    public bool IsConnected => _client?.Connected ?? false;
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; } = 23;

    public event Action<string>? DataReceived;
    public event Action<bool>? ConnectionChanged;

    public async Task ConnectAsync(string host, int port = 23, CancellationToken ct = default)
    {
        if (IsConnected)
            Disconnect();

        Host = host;
        Port = port;
        _disconnecting = false;

        _client = new TcpClient();
        await _client.ConnectAsync(host, port, ct);
        _stream = _client.GetStream();

        _readCts = new CancellationTokenSource();
        _ = ReadLoopAsync(_readCts.Token);

        ConnectionChanged?.Invoke(true);
    }

    public void Disconnect()
    {
        if (_disconnecting) return;
        _disconnecting = true;

        // Cancel the read loop first so it stops using the stream
        _readCts?.Cancel();

        // Dispose stream before nulling — prevents race with background read
        _stream?.Dispose();
        _stream = null;

        _client?.Close();
        _client = null;

        _readCts?.Dispose();
        _readCts = null;

        ConnectionChanged?.Invoke(false);
    }

    public async Task SendAsync(string text, CancellationToken ct = default)
    {
        var stream = _stream;
        if (stream == null || !IsConnected)
            return;

        try
        {
            var bytes = Encoding.ASCII.GetBytes(text + "\r\n");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            await stream.WriteAsync(bytes, timeoutCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var stream = _stream;
                if (stream == null) break;
                if (!IsConnected) break;

                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, ct);
                }
                catch (ObjectDisposedException) { break; }
                catch (NullReferenceException) { break; }

                if (bytesRead == 0) break;

                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0xFF && i + 2 < bytesRead)
                    {
                        if (sb.Length > 0)
                        {
                            DataReceived?.Invoke(CleanOutput(sb.ToString()));
                            sb.Clear();
                        }
                        HandleTelnetCommand(buffer[i + 1], buffer[i + 2]);
                        i += 2;
                    }
                    else if (buffer[i] == 0xFF)
                    {
                        break;
                    }
                    else
                    {
                        sb.Append((char)buffer[i]);
                    }
                }

                if (sb.Length > 0)
                {
                    DataReceived?.Invoke(CleanOutput(sb.ToString()));
                    sb.Clear();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (NullReferenceException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Telnet read loop error: {ex}");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                Disconnect();
        }
    }

    private void HandleTelnetCommand(byte command, byte option)
    {
        var stream = _stream;
        if (stream == null || _disconnecting) return;

        byte[]? reply = command switch
        {
            0xFD => new byte[] { 0xFF, 0xFC, option }, // DO  → WONT
            0xFB => new byte[] { 0xFF, 0xFE, option }, // WILL → DONT
            _ => null
        };

        if (reply != null)
        {
            try { stream.Write(reply); }
            catch { /* connection already closed — ignore */ }
        }
    }

    /// <summary>
    /// Strip ANSI escape sequences and null bytes from Telnet output.
    /// </summary>
    private static string CleanOutput(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var result = System.Text.RegularExpressions.Regex.Replace(raw, @"\x1B\[[0-9;]*[a-zA-Z]", "");
        result = result.Replace("\0", "");
        return result;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
