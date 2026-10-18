using System.Net;
using System.Net.Sockets;
using System.Text;

internal class Program
{
    private const int TcpPort = 7100;
    private const int UdpPort = 7101;
    private static readonly string SharedFolder =
        Path.Combine(AppContext.BaseDirectory, "Shared");

    private static readonly SemaphoreSlim BroadcastLock = new(1, 1);

    private static async Task Main()
    {
        Directory.CreateDirectory(SharedFolder);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        using var tcpServer = new TcpListener(IPAddress.Any, TcpPort);
        using var udp = new UdpClient { EnableBroadcast = true };
        tcpServer.Start();

        using var watcher = new FileSystemWatcher(SharedFolder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, e) => QueueBroadcast("CREATE", e.Name);
        watcher.Changed += (_, e) => QueueBroadcast("CHANGE", e.Name);
        watcher.Deleted += (_, e) => QueueBroadcast("DELETE", e.Name);
        watcher.Renamed += (_, e) =>
        {
            QueueBroadcast("DELETE", e.OldName);
            QueueBroadcast("CREATE", e.Name);
        };

        Console.WriteLine($"Sync Server запущено.");
        Console.WriteLine($"Папка сервера: {SharedFolder}");
        Console.WriteLine($"TCP: {TcpPort}, UDP broadcast: {UdpPort}");
        Console.WriteLine("Додайте/змініть/видаліть файл у цій папці. Ctrl+C — завершити.");

        void QueueBroadcast(string action, string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || !IsSafeFileName(name))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    // FileSystemWatcher може видати декілька подій для одного запису.
                    await Task.Delay(150);
                    if (action != "DELETE" && !File.Exists(Path.Combine(SharedFolder, name)))
                        return;

                    string ip = GetLocalIPv4Address();
                    string payload = $"{action}|{name}|{ip}|{TcpPort}";
                    byte[] bytes = Encoding.UTF8.GetBytes(payload);

                    await BroadcastLock.WaitAsync(cts.Token);
                    try
                    {
                        await udp.SendAsync(bytes, new IPEndPoint(IPAddress.Broadcast, UdpPort));
                    }
                    finally
                    {
                        BroadcastLock.Release();
                    }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {payload}");
                }
                catch (OperationCanceledException)
                {
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"Помилка broadcast: {ex.Message}");
                }
            });
        }

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await tcpServer.AcceptTcpClientAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = Task.Run(() => HandleFileRequest(client, cts.Token));
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Помилка TCP: {ex.Message}");
        }
        finally
        {
            tcpServer.Stop();
        }
    }

    private static async Task HandleFileRequest(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                using NetworkStream stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true
                };

                string? name = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                if (string.IsNullOrWhiteSpace(name) || !IsSafeFileName(name))
                {
                    await writer.WriteLineAsync("ERROR");
                    return;
                }

                string path = Path.Combine(SharedFolder, name);
                if (!File.Exists(path))
                {
                    await writer.WriteLineAsync("NOT_FOUND");
                    return;
                }

                byte[] fileBytes = await File.ReadAllBytesAsync(path, token);
                await writer.WriteLineAsync($"OK|{fileBytes.Length}");
                await stream.WriteAsync(fileBytes, token);

                Console.WriteLine($"Відправлено файл: {name} ({fileBytes.Length} байт)");
            }
            catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or OperationCanceledException)
            {
                Console.WriteLine($"Помилка передачі файлу: {ex.Message}");
            }
        }
    }

    private static bool IsSafeFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name != "." && name != ".." &&
        Path.GetFileName(name) == name &&
        !name.Contains(Path.DirectorySeparatorChar) &&
        !name.Contains(Path.AltDirectorySeparatorChar) &&
        !name.Contains('|') &&
        !name.Contains('\r') && !name.Contains('\n');

    private static string GetLocalIPv4Address()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endpoint)
                return endpoint.Address.ToString();
        }
        catch
        {
        }

        return "127.0.0.1";
    }
}
