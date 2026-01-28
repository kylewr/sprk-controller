using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SPRK.Core;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SPRK.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly RobotConnection connection;
    private const string VersionStr = "2.0";

    // Track pressed keys for three-finger combo
    private readonly HashSet<Key> pressedKeys = [];

    [ObservableProperty]
    private string _hostname = "192.168.1.111";

    [ObservableProperty]
    private int _port = 8007;

    [ObservableProperty]
    private string _robotInfoText = "";

    [ObservableProperty]
    private string _robotStateText = "Disconnected";

    [ObservableProperty]
    private IBrush _robotStateColor = Brushes.LightGray;

    [ObservableProperty]
    private string _connectButtonText = "Connect to Robot";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _canConnect = true;

    [ObservableProperty]
    private bool _teleopVisible = false;

    [ObservableProperty]
    private bool _autonVisible = false;

    [ObservableProperty]
    private bool _disableVisible = false;

    [ObservableProperty]
    private bool _teleopEnabled = false;

    [ObservableProperty]
    private bool _autonEnabled = false;

    [ObservableProperty]
    private bool _disableEnabled = false;

    [ObservableProperty]
    private bool _killEnabled = false;

    [ObservableProperty]
    private string? _selectedAuton;

    [ObservableProperty]
    private bool _robotSimulated = false;

    [ObservableProperty]
    private bool _useKeyboard = true;

    [ObservableProperty]
    private bool _cameraEnabled = false;

    [ObservableProperty]
    private string _robotStatusText = "Robot Disconnected.";

    [ObservableProperty]
    private IBrush _robotStatusColor = Brushes.Red;

    [ObservableProperty]
    private string _controllerStatusText = "Keyboard Input";

    [ObservableProperty]
    private IBrush _controllerStatusColor = Brushes.MediumAquamarine;

    [ObservableProperty]
    private string _consoleText = "";

    public ObservableCollection<string> AutonList { get; } = new();
    public ObservableCollection<ConsoleMessage> ConsoleMessages { get; } = new();

    private bool isInTele = false;

    public MainViewModel()
    {
        connection = new RobotConnection();

        // Wire up events
        connection.OnMessage += (msg, color) => Dispatcher.UIThread.Post(() => AddConsoleText(msg, color));
        connection.OnStateChange += (state) => Dispatcher.UIThread.Post(() => HandleStateChange(state));
        connection.OnRobotInfo += (info, autons, flags) => Dispatcher.UIThread.Post(() => HandleRobotInfo(info, autons, flags));
        connection.OnConnected += () => Dispatcher.UIThread.Post(HandleConnected);
        connection.OnDisconnected += () => Dispatcher.UIThread.Post(HandleDisconnected);

        // Initial console messages
        AddConsoleText(">> Welcome to the SPRK Controller!", ConsoleColor.Cyan);
        AddConsoleText($">> Version {VersionStr}", ConsoleColor.Magenta);
        AddConsoleText(">> Written by Kyle Rush", ConsoleColor.Magenta);
    }

    public void HandleKeyDown(Key key)
    {
        pressedKeys.Add(key);

        // Three-finger combo: [ ] \ to enable teleop
        if (TeleopEnabled && 
            pressedKeys.Contains(Key.OemOpenBrackets) && 
            pressedKeys.Contains(Key.OemCloseBrackets) && 
            pressedKeys.Contains(Key.OemBackslash))
        {
            SendTeleopCommand.Execute(null);
        }
    }

    public void HandleKeyUp(Key key)
    {
        pressedKeys.Remove(key);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!IsConnected)
        {
            CanConnect = false;
            ConnectButtonText = "Connecting...";
            RobotStateText = "Connecting";
            RobotStateColor = Brushes.SandyBrown;

            bool success = await connection.Connect(Hostname, Port, VersionStr);
            
            if (!success)
            {
                ConnectButtonText = "Connect to Robot";
                RobotStateText = "Disconnected";
                RobotStateColor = Brushes.LightGray;
                CanConnect = true;
            }
            else
            {
                IsConnected = true;
                ConnectButtonText = "Disconnect";
                CanConnect = true;
            }
        }
        else
        {
            connection.Disconnect();
        }
    }

    [RelayCommand]
    private async Task SendTeleopAsync()
    {
        await connection.SendCommand("tele");
    }

    [RelayCommand]
    private async Task SendAutonAsync()
    {
        await connection.SendCommand("auto");
    }

    [RelayCommand]
    private async Task SendDisableAsync()
    {
        await connection.SendCommand("dis");
    }

    [RelayCommand]
    private async Task KillRobotAsync()
    {
        AddConsoleText("Killing Robot.", ConsoleColor.Red);
        await connection.SendCommand("exit");
        await Task.Delay(100);
        connection.Disconnect();
    }

    [RelayCommand]
    private void ClearConsole()
    {
        ConsoleMessages.Clear();
        ConsoleText = "";
    }

    [RelayCommand]
    private void ShowAbout()
    {
        AddConsoleText($"Written by Kyle Rush.\nVersion {VersionStr}\nIcon(s) from Freepik", ConsoleColor.Cyan);
    }

    [RelayCommand]
    private void RescanJoystick()
    {
        // TODO: Implement joystick scanning for Linux
        AddConsoleText("[SPRK CONTROLLER] Joystick rescan not yet implemented on Linux.", ConsoleColor.Yellow);
    }

    [RelayCommand]
    private void LaunchCamera()
    {
        // TODO: Implement camera launch
        AddConsoleText("[CAMERA] Camera stream not yet implemented.", ConsoleColor.Yellow);
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/kylewr/sprk-controller",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private async Task SelectAuton(string autonName)
    {
        if (!string.IsNullOrEmpty(autonName) && IsConnected)
        {
            SelectedAuton = autonName;
            await connection.SendCommand($"se-auto,{autonName}");
            AddConsoleText($"[AUTON] Selected: {autonName}", ConsoleColor.Green);
        }
    }

    private void HandleStateChange(string state)
    {
        if (state == "DISABLE")
        {
            RobotStateText = RobotSimulated ? "SIM - DISABLED" : "DISABLED";
            RobotStateColor = Brushes.Red;
            isInTele = false;
            UpdateButtonStates(false);
        }
        else if (state == "TELEOP")
        {
            RobotStateText = RobotSimulated ? "SIM - TELEOP" : "TELEOP";
            RobotStateColor = Brushes.Green;
            isInTele = true;
            UpdateButtonStates(true);
        }
        else if (state == "AUTONOMOUS")
        {
            RobotStateText = RobotSimulated ? "SIM - AUTONOMOUS" : "AUTONOMOUS";
            RobotStateColor = Brushes.Yellow;
            isInTele = false;
            UpdateButtonStates(true);
        }
    }

    private void HandleRobotInfo(string info, List<string> autons, List<string> flags)
    {
        RobotInfoText += info + "\r\n";

        AutonList.Clear();
        foreach (var auton in autons)
        {
            AutonList.Add(auton);
        }

        RobotSimulated = flags.Contains("sim");
        CameraEnabled = flags.Contains("camera") && !RobotSimulated;
    }

    private void HandleConnected()
    {
        TeleopVisible = true;
        AutonVisible = true;
        DisableVisible = true;
        KillEnabled = true;
        RobotStatusText = "Robot Connected.";
        RobotStatusColor = Brushes.Green;
    }

    private void HandleDisconnected()
    {
        RobotStateText = "Disconnected";
        RobotStateColor = Brushes.LightGray;
        IsConnected = false;
        ConnectButtonText = "Connect to Robot";
        TeleopVisible = false;
        AutonVisible = false;
        DisableVisible = false;
        KillEnabled = false;
        TeleopEnabled = false;
        AutonEnabled = false;
        DisableEnabled = false;
        RobotStatusText = "Robot Disconnected.";
        RobotStatusColor = Brushes.Red;
    }

    private void UpdateButtonStates(bool enabled)
    {
        if (enabled)
        {
            TeleopEnabled = !isInTele;
            AutonEnabled = !isInTele;
            DisableEnabled = true;
        }
        else
        {
            TeleopEnabled = true;
            AutonEnabled = true;
            DisableEnabled = false;
        }
    }

    private void AddConsoleText(string message, ConsoleColor color)
    {
        if (string.IsNullOrEmpty(message))
            return;

        var brush = color switch
        {
            ConsoleColor.Cyan => Brushes.Cyan,
            ConsoleColor.DarkCyan => Brushes.DarkCyan,
            ConsoleColor.Magenta => Brushes.Magenta,
            ConsoleColor.Red => Brushes.Red,
            ConsoleColor.Green => Brushes.LawnGreen,
            ConsoleColor.Yellow => Brushes.Yellow,
            ConsoleColor.Blue => Brushes.DodgerBlue,
            ConsoleColor.DarkYellow => Brushes.Orange,
            _ => Brushes.LightGray
        };
        ConsoleMessages.Add(new ConsoleMessage(message, brush));
        ConsoleText += message + "\n";
    }
}

// Simple class for console messages with color
public class ConsoleMessage
{
    public string Text { get; }
    public IBrush Color { get; }

    public ConsoleMessage(string text, IBrush color)
    {
        Text = text;
        Color = color;
    }
}
