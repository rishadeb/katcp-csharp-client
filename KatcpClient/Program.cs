using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


public class KatcpSensor <T>
{
    public double Timestamp { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Units { get; set; }
    public string? Type {get; set; }
    public T? Value { get; set; }
}

public class KATCPClient
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient _tcpClient;
    private NetworkStream _networkStream;
    private CancellationTokenSource _cancellationTokenSource;
    private Task _receiveTask;

    public KATCPClient(string host, int port)
    {
        _host = host;
        _port = port;
        _tcpClient = new TcpClient();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public async Task ConnectAsync()
    {
        await _tcpClient.ConnectAsync(_host, _port);
        _networkStream = _tcpClient.GetStream();
        Console.WriteLine($"Connected to {_host}:{_port}");

        // Read initial informs
        await ReadInitialInformsAsync();

        // Start receiving messages
        _receiveTask = ReceiveRepliesAsync();
    }

    private async Task ReadInitialInformsAsync()
    {
        byte[] buffer = new byte[4096]; // 4 KB buffer size
        StringBuilder informs = new StringBuilder();

        while (true)
        {
            int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token);
            if (bytesRead == 0) break; // End of stream

            informs.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

            // Check if we have received all initial informs
            if (informs.ToString().Contains("#version-connect katcp-protocol"))
            {
                break;
            }
        }

        // Process each line of informs
        string[] messages = informs.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var message in messages)
        {
            HandleMessage(message);
        }
    }

    public async Task SendRequestAsync(string request)
    {
        if (_networkStream == null)
        {
            throw new InvalidOperationException("Not connected to the server.");
        }

        byte[] requestBytes = Encoding.UTF8.GetBytes(request + "\n");
        await _networkStream.WriteAsync(requestBytes, 0, requestBytes.Length, _cancellationTokenSource.Token);
        Console.WriteLine($"Sent request: {request}");
    }

    private async Task ReceiveRepliesAsync()
    {
        byte[] buffer = new byte[4096]; // 4 KB buffer size
        StringBuilder reply = new StringBuilder();

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token);
            if (bytesRead == 0) break; // End of stream

            reply.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

            // Check if we have received a complete message
            if (reply.ToString().Contains("\n"))
            {
                string response = reply.ToString();
                reply.Clear();
                Console.WriteLine($"Received reply: {response}");
                HandleMessage(response);
            }
        }
    }
    public void HandleMessage(string message)
    {
        switch (message[0])
        {
            case '?':
                Console.WriteLine($"Request: {message}");
                break;
            case '!':
                Console.WriteLine($"Reply: {message}");
                break;
            case '#':
                Console.WriteLine($"Inform: {message}");
                break;
            default:
                Console.WriteLine($"Unknown message type: {message}");
                break;
        }
    }

    public async Task CloseAsync()
    {
        _cancellationTokenSource.Cancel();
        _networkStream?.Close();
        _tcpClient?.Close();
        if (_receiveTask != null)
        {
            try 
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Receive task cancelled");
            }
        }
        Console.WriteLine("Connection closed");
    }

    public static async Task Main(string[] args)
    {
        var client = new KATCPClient("127.0.0.1", 4321);

        try
        {
            await client.ConnectAsync();

            // Continuously read user input and send requests
            while (true)
            {
                string request = Console.ReadLine();
                if (string.IsNullOrEmpty(request))
                {
                    await client.CloseAsync();
                    break; // Exit on empty input
                }
                await client.SendRequestAsync(request);
            }
        }
        finally
        {
            await client.CloseAsync();
        }
    }
}
