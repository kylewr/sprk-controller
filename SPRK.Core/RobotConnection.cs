using System.Net.Sockets;
using System.Text;

namespace SPRK.Core;

public class RobotConnection
{
    public event Action<string, ConsoleColor>? OnMessage;
    public event Action<string>? OnStateChange;  // "DISABLE", "TELEOP", "AUTONOMOUS"
    public event Action<string, List<string>, List<string>>? OnRobotInfo; // info, autons, flags
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    private TcpClient? client;
    private NetworkStream? stream;
    private Thread? receiverThread;
    private bool isConnected;

    public bool IsConnected => isConnected;
    public bool IsSimulated { get; private set; }
    public bool HasCamera { get; private set; }

    public async Task<bool> Connect(string hostname, int port, string version)
    {
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(hostname, port);
            stream = client.GetStream();
            isConnected = true;

            receiverThread = new Thread(() => RunReceiver()) { IsBackground = true };
            receiverThread.Start();

            await Task.Delay(100);
            await SendCommand($"init,{version}");

            OnMessage?.Invoke($"[SOCKET] Connected to {hostname}:{port}.", ConsoleColor.Green);
            OnConnected?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            OnMessage?.Invoke($"[SOCKET] Error: {ex.Message}", ConsoleColor.Red);
            return false;
        }
    }

    public async Task SendCommand(string command)
    {
        if (stream != null && isConnected)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command + "\n");
            await stream.WriteAsync(bytes);
        }
    }

    public void Disconnect()
    {
        isConnected = false;
        stream?.Close();
        client?.Close();
        receiverThread?.Join(1000);
        OnMessage?.Invoke("[SOCKET] Connection closed.", ConsoleColor.Magenta);
    }

    private void RunReceiver()
    {
        try
        {
            OnMessage?.Invoke("[RECIEVER] Connected.", ConsoleColor.Magenta);
            while (isConnected && stream != null)
            {
                byte[] buffer = new byte[256];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                ProcessMessage(message);
            }
        }
        catch (Exception ex)
        {
            OnMessage?.Invoke($"[RECIEVER] Reciever Error: {ex.Message}", ConsoleColor.Red);
        }
        finally
        {
            isConnected = false;
            OnMessage?.Invoke("[RECIEVER] Reciever closed.", ConsoleColor.Magenta);
            OnDisconnected?.Invoke();
        }
    }

    private void ProcessMessage(string message)
    {
        if (message.StartsWith("[STATE] "))
        {
            OnStateChange?.Invoke(message[8..]);
        }
        else if (message.StartsWith("[ROBOTINFO]"))
        {
            ParseRobotInfo(message);
        }
        else
        {
            ParseConsoleMessage(message);
        }
    }

    private void ParseRobotInfo(string message)
    {
        try
        {
            var splitted = message.Split("[SIG]");
            List<string> autons = new();
            List<string> flags = new();
            
            // FIX: Check array bounds properly like original
            if (splitted.Length > 1 && splitted[1].Contains("[AUTONS]"))
            {
                autons.AddRange(splitted[1].Replace("[AUTONS]", "").Split(',').Where(s => !string.IsNullOrEmpty(s)));
            }
            if (splitted.Length > 2 && splitted[2].Contains("[FLAGS]"))
            {
                flags.AddRange(splitted[2].Replace("[FLAGS]", "").Split(',').Where(s => !string.IsNullOrEmpty(s)));
                
                IsSimulated = flags.Contains("sim");
                HasCamera = flags.Contains("camera") && !IsSimulated;
            }
            
            string info = splitted[0].Replace("\n", "\r\n").Replace("[ROBOTINFO]", "");
            OnRobotInfo?.Invoke(info, autons, flags);
        }
        catch (Exception ex)
        {
            OnMessage?.Invoke($"[RECIEVER] Error: {ex.Message}", ConsoleColor.Red);
            // Fallback like original
            string fallbackInfo = message.Replace("[ROBOTINFO]", "").Replace("\n", "\r\n");
            OnRobotInfo?.Invoke(fallbackInfo, new List<string>(), new List<string>());
        }
    }

    private void ParseConsoleMessage(string message)
    {
        foreach (string line in message.Split("%NL%"))
        {
            string text = line.Replace("%NL%", "");
            ConsoleColor color = ConsoleColor.White;

            // Match all colors from original
            if (line.StartsWith("%GREEN%")) { text = text.Replace("%GREEN%", ""); color = ConsoleColor.Green; }
            else if (line.StartsWith("%ORANGE%")) { text = text.Replace("%ORANGE%", ""); color = ConsoleColor.DarkYellow; }
            else if (line.StartsWith("%BLUE%")) { text = text.Replace("%BLUE%", ""); color = ConsoleColor.Blue; }
            else if (line.StartsWith("%YELLOW%")) { text = text.Replace("%YELLOW%", ""); color = ConsoleColor.Yellow; }
            else if (line.StartsWith("%RED%")) { text = text.Replace("%RED%", ""); color = ConsoleColor.Red; }

            if (!string.IsNullOrEmpty(text))
                OnMessage?.Invoke(text, color);
        }
    }
}