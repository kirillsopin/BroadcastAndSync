using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal class Program
{
    private const int TcpPort = 7000;
    private const int UdpPort = 7001;

    private static readonly ConcurrentDictionary<int, string> Messages = new();
    private static readonly string[] Phrases =
    {
        "Server started",
        "New client connected",
        "CPU usage is high",
        "Backup completed",
        "System temperature is normal",
        "Database synchronized"
    };

    private static async Task Main()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        using var tcpServer = new TcpListener(IPAddress.Any, TcpPort);
        using var udpSender = new UdpClient { EnableBroadcast = true };

        tcpServer.Start();
        Console.WriteLine($"Broadcast Server запущено. TCP: {TcpPort}, UDP broadcast: {UdpPort}");
        Console.WriteLine("Ctrl+C — завершити.");

        Task tcpTask = HandleTcpClients(tcpServer, cts.Token);
        Task broadcastTask = SendBroadcasts(udpSender, cts.Token);

        try
        {
            await Task.WhenAll(tcpTask, broadcastTask);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            tcpServer.Stop();
        }
    }

    private static async Task HandleTcpClients(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(async () =>
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

                        string? input = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        if (int.TryParse(input, out int id) && Messages.TryGetValue(id, out string? message))
                        {
                            await writer.WriteLineAsync(message);
                            Console.WriteLine($"TCP: клієнт {client.Client.RemoteEndPoint}, ID={id}");
                        }
                        else
                        {
                            await writer.WriteLineAsync("Message ID not found");
                        }
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or OperationCanceledException)
                    {
                        Console.WriteLine($"Помилка клієнта: {ex.Message}");
                    }
                }
            });
        }
    }

    private static async Task SendBroadcasts(UdpClient udp, CancellationToken token)
    {
        var destination = new IPEndPoint(IPAddress.Broadcast, UdpPort);
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Random.Shared.Next(2000, 7001), token);

                int id;
                do
                {
                    id = Random.Shared.Next(100, 1000000);
                } while (Messages.ContainsKey(id));

                string message = Phrases[Random.Shared.Next(Phrases.Length)];
                Messages[id] = message;

                // Залишаємо в пам'яті лише останні 500 повідомлень.
                if (Messages.Count > 500)
                {
                    foreach (int oldId in Messages.Keys.Take(100))
                        Messages.TryRemove(oldId, out _);
                }

                string serverIp = GetLocalIPv4Address();
                string payload = $"{serverIp}:{TcpPort}:{id}";
                byte[] data = Encoding.UTF8.GetBytes(payload);
                await udp.SendAsync(data, destination);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Broadcast: {payload} | {message}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"UDP помилка: {ex.Message}");
            }
        }
    }

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
