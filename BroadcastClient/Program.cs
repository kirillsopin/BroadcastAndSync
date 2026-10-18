using System.Net;
using System.Net.Sockets;
using System.Text;

internal class Program
{
    private const int UdpPort = 7001;

    private static async Task Main()
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, UdpPort));

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"Broadcast Client слухає UDP-порт {UdpPort}.");
        Console.WriteLine("Очікування повідомлень... Ctrl+C — завершити.");

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result = await udp.ReceiveAsync(cts.Token);
                string payload = Encoding.UTF8.GetString(result.Buffer).Trim();
                string[] parts = payload.Split(':');

                if (parts.Length != 3 ||
                    !IPAddress.TryParse(parts[0], out IPAddress? ip) ||
                    !int.TryParse(parts[1], out int tcpPort) ||
                    !int.TryParse(parts[2], out int id) ||
                    tcpPort is < 1 or > 65535)
                {
                    Console.WriteLine($"Некоректне broadcast-повідомлення: {payload}");
                    continue;
                }

                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Отримано: {payload}");

                try
                {
                    using var tcp = new TcpClient();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));

                    await tcp.ConnectAsync(ip, tcpPort, timeout.Token);
                    using NetworkStream stream = tcp.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
                    {
                        AutoFlush = true
                    };

                    await writer.WriteLineAsync(id.ToString());
                    string? message = await reader.ReadLineAsync().WaitAsync(timeout.Token);
                    Console.WriteLine($"Повідомлення #{id}: {message ?? "Сервер не повернув текст"}");
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or TimeoutException)
                {
                    Console.WriteLine($"Не вдалося отримати текст через TCP: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine("Broadcast Client завершив роботу.");
    }
}
