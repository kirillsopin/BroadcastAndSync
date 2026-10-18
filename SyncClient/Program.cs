using System.Net;
using System.Net.Sockets;
using System.Text;

internal class Program
{
    private const int UdpPort = 7101;
    private static readonly string ClientFolder =
        Path.Combine(AppContext.BaseDirectory, "Synced");

    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private static async Task Main()
    {
        Directory.CreateDirectory(ClientFolder);

        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, UdpPort));

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine("Sync Client запущено.");
        Console.WriteLine($"Папка клієнта: {ClientFolder}");
        Console.WriteLine($"Очікування broadcast на UDP-порті {UdpPort}. Ctrl+C — завершити.");

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result = await udp.ReceiveAsync(cts.Token);
                string payload = Encoding.UTF8.GetString(result.Buffer).Trim();
                string[] parts = payload.Split('|');

                if (parts.Length != 4 ||
                    !int.TryParse(parts[3], out int tcpPort) ||
                    tcpPort is < 1 or > 65535 ||
                    !IPAddress.TryParse(parts[2], out IPAddress? serverIp) ||
                    !IsSafeFileName(parts[1]))
                {
                    Console.WriteLine($"Некоректне повідомлення: {payload}");
                    continue;
                }

                string action = parts[0].ToUpperInvariant();
                string name = parts[1];
                string localPath = Path.Combine(ClientFolder, name);

                try
                {
                    if (action == "DELETE")
                    {
                        await FileLock.WaitAsync(cts.Token);
                        try
                        {
                            if (File.Exists(localPath))
                                File.Delete(localPath);
                        }
                        finally
                        {
                            FileLock.Release();
                        }

                        Console.WriteLine($"Видалено: {name}");
                    }
                    else if (action is "CREATE" or "CHANGE")
                    {
                        byte[] fileBytes = await DownloadFile(serverIp, tcpPort, name, cts.Token);

                        await FileLock.WaitAsync(cts.Token);
                        try
                        {
                            string tempPath = localPath + ".download";
                            await File.WriteAllBytesAsync(tempPath, fileBytes, cts.Token);
                            File.Move(tempPath, localPath, true);
                        }
                        finally
                        {
                            FileLock.Release();
                        }

                        Console.WriteLine($"{action}: синхронізовано {name} ({fileBytes.Length} байт)");
                    }
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or TimeoutException)
                {
                    if (!cts.IsCancellationRequested)
                        Console.WriteLine($"Помилка синхронізації {name}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine("Sync Client завершив роботу.");
    }

    private static async Task<byte[]> DownloadFile(
        IPAddress serverIp, int port, string name, CancellationToken token)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        await client.ConnectAsync(serverIp, port, timeout.Token);
        using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        await writer.WriteLineAsync(name);
        string? header = await reader.ReadLineAsync().WaitAsync(timeout.Token);

        if (header == "NOT_FOUND")
            throw new IOException("Файл уже відсутній на сервері.");

        if (header == null || !header.StartsWith("OK|", StringComparison.Ordinal) ||
            !int.TryParse(header.AsSpan(3), out int length) || length < 0)
            throw new IOException("Некоректна відповідь сервера.");

        byte[] data = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int read = await stream.ReadAsync(data.AsMemory(offset, length - offset), timeout.Token);
            if (read == 0)
                throw new EndOfStreamException("Передача файлу перервалася.");
            offset += read;
        }

        return data;
    }

    private static bool IsSafeFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name != "." && name != ".." &&
        Path.GetFileName(name) == name &&
        !name.Contains(Path.DirectorySeparatorChar) &&
        !name.Contains(Path.AltDirectorySeparatorChar) &&
        !name.Contains('|') &&
        !name.Contains('\r') && !name.Contains('\n');
}
