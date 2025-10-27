using SharpDX.XInput;
using System.CodeDom;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace SPRK
{
    public partial class WIN_MAIN : Form
    {
        private readonly string VersionStr;

        private readonly Color defaultConsoleColor;

        private Controller? controller;
        private bool bypassJoystick = false;

        private string hostName;
        private int port;

        private bool sessionActive = false;
        private Thread? socketThread = null;
        private Thread? socketReciever = null;

        private bool socketConnected = false;
        private bool robotSimulated = false;

        private bool teleopRequest = false;
        private bool autoRequest = false;
        private bool disableRequest = false;
        private bool killRequest = false;
        private bool changeAutoRequest = false;
        private bool cameraStartRequest = false;
        private bool cameraStopRequest = false;

        private bool isInTele = false;
        private string selectedAutonName = "";

        private readonly TSMCollection connectControlModifications = new();
        private readonly TSMCollection disconnectControlModifications = new();

        private HashSet<Keys> pressedKeys = [];

        private readonly Dictionary<Keys, string> keyMap = new()
        {
            {Keys.I, "DPADUP"},
            {Keys.K, "DPADDOWN"},
            {Keys.J, "DPADLEFT"},
            {Keys.L, "DPADRIGHT"},
            {Keys.U, "LEFTSHOULDER"},
            {Keys.O, "RIGHTSHOULDER"},
            {Keys.Oemcomma, "BACK"},
            {Keys.OemPeriod, "START"},
            {Keys.Z, "A"},
            {Keys.X, "B"},
            {Keys.C, "X"},
            {Keys.V, "Y"},
        };

        public WIN_MAIN()
        {
            InitializeComponent();

            VersionStr = Properties.Resources.AppVersion;
            ss_label.Text = $"SPRK Controller {VersionStr}";
            defaultConsoleColor = console.ForeColor;

            ss_label.Image = GetImageFromBytes(Properties.Resources.AppPNG);

            AddConsoleText(">> Welcome to the SPRK Controller!", Color.DarkCyan);
            AddConsoleText($">> Version {VersionStr}", Color.BlueViolet);
            AddConsoleText(">> Written by Kyle Rush", Color.BlueViolet);
            if (Properties.Resources.BetaWarning == "true")
            {
                AddConsoleText(">> Warning! This is a BETA version. Use with caution.", Color.Red);
            }

            AddConsoleText("", Color.White);

            cb_hostname.Text = MainSettings.Default.Hostname;
            foreach (var item in MainSettings.Default.SavedHosts)
            {
                if (item == null)
                {
                    continue;
                }
                cb_hostname.Items.Add(item);
            }
            ;
            nud_port.Value = MainSettings.Default.Port;
            hostName = cb_hostname.Text;
            port = (int)nud_port.Value;
            backgroundPrefs.Checked = MainSettings.Default.UsingBackground;

            ss_controller_Click(null, null); // invoke a controller scan

            connectControlModifications = new TSMCollection([
                new ThreadSafeModification<Button>(b_connect, (c) =>
                {
                    c.Enabled = true;
                    ActiveControl = c;
                    ms_auton.Enabled = true;
                    robot_auton.Items.Clear();
                }),
                TSMPresets.SetEnabled(b_kill, true),
                TSMPresets.SetVisible(b_teleop, true),
                TSMPresets.SetVisible(b_auton, true),
                TSMPresets.SetVisible(b_disable, true),
            ]);

            disconnectControlModifications = new TSMCollection([
                new ThreadSafeModification<RichTextBox>(console, (c) => {
                    ms_auton.Enabled = false;
                    ms_cams.Enabled = false;
                    c.SelectionStart = c.Text.Length;
                    c.ScrollToCaret();
                }),
                new ThreadSafeModification<Button>(b_connect, (c) => {
                    c.Enabled = true;
                    c.Text = "&Connect to Robot";
                }),
                new ThreadSafeModification<TextBox>(robotState, (c) => {
                    c.Text = "Disconnected";
                    c.BackColor = Color.FromKnownColor(KnownColor.Control);
                }),
                TSMPresets.SetEnabled(cb_hostname, true),
                TSMPresets.SetEnabled(nud_port, true),
                new ThreadSafeModification<Button>(b_teleop, (c) => {
                    c.Enabled = false;
                    c.Visible = false;
                    c.BackColor = Color.FromKnownColor(KnownColor.Control);
                }),
                new ThreadSafeModification<Button>(b_auton, (c) => {
                    c.Enabled = false;
                    c.Visible = false;
                    c.BackColor = Color.FromKnownColor(KnownColor.Control);
                }),
                new ThreadSafeModification<Button>(b_disable, (c) => {
                    c.Enabled = false;
                    c.Visible = false;
                    c.BackColor = Color.FromKnownColor(KnownColor.Control);
                }),
                TSMPresets.SetEnabled(b_kill, false),
                new ThreadSafeModification<WIN_MAIN>(this, (_) => {
                    foreach (Form openForm in Application.OpenForms) {
                        if (openForm is WIN_CameraStream cameraStream) {
                            try {
                                cameraStream.Close();
                                break;
                            } catch (Exception ex) {
                                WriteConsole($"[CAMERA] Error closing camera stream: {ex.Message}", Color.Red);
                            }
                        }
                    }
                }),
                //TSMPresets.SetVisible(b_startCode, true)
            ]);
        }

        private void RunSocketThread()
        {
            using TcpClient client = new();
            try
            {
                if (!controller!.IsConnected && !bypassJoystick)
                {
                    joystick_bypass.Checked = true;
                    joystick_bypass_Click(null, null);
                    WriteConsole(">> [SPRK CONTROLLER] Automatically switched using Keyboard input.\n", Color.Orange);
                }

                WriteConsole($"[SPRK CONTROLLER] The current mode of input is: {(bypassJoystick ? "Keyboard" : "Joystick")}", Color.Aquamarine);

                client.Connect(hostName, port);
                using NetworkStream stream = client.GetStream();

                socketConnected = true;

                socketReciever = new Thread(() => RunSocketReciever(stream))
                {
                    IsBackground = true
                };
                socketReciever.Start();

                Thread.Sleep(100);

                WriteData(stream, $"init,{VersionStr}");

                // update UI elements (safely)
                connectControlModifications.Apply();
                SetEnableButton(false);
                WriteConsole($"[SOCKET] Connected to {hostName}:{port}.", Color.Green);

                if (!MainSettings.Default.SavedHosts.Contains(hostName))
                {
                    MainSettings.Default.SavedHosts.Add(hostName);
                    MainSettings.Default.Save();
                }

                HashSet<GamepadButtonFlags> pressedButtons = [];
                HashSet<Keys> sentKeys = [];

                ss_robot.Text = "Robot connected.";
                ss_robot.BackColor = Color.Green;

                while (socketConnected)
                {
                    if (teleopRequest)
                    {
                        WriteData(stream, "tele");
                        teleopRequest = false;
                        continue;
                    }
                    if (autoRequest)
                    {
                        WriteData(stream, "auto");
                        autoRequest = false;
                        continue;
                    }
                    if (disableRequest)
                    {
                        WriteData(stream, "dis");
                        disableRequest = false;
                        continue;
                    }
                    if (killRequest)
                    {
                        WriteConsole("Killing Robot.", Color.LightCoral);
                        killRequest = false;
                        WriteData(stream, "exit");
                        Thread.Sleep(100);
                        break;
                    }
                    if (changeAutoRequest)
                    {
                        WriteData(stream, "se-auto," + selectedAutonName);
                        changeAutoRequest = false;
                        continue;
                    }
                    if (cameraStartRequest)
                    {
                        WriteData(stream, "cam-start");
                        cameraStartRequest = false;
                        continue;
                    }
                    if (cameraStopRequest)
                    {
                        WriteData(stream, "cam-stop");
                        cameraStopRequest = false;
                        continue;
                    }

                    if (isInTele)
                    {
                        if (!bypassJoystick)
                        {

                            var state = controller.GetState();
                            var gamepad = state.Gamepad;

                            // Only send joystick data in teleop

                            // Send stick data
                            int leftX = (int)NormalizeStick(gamepad.LeftThumbX);
                            int leftY = (int)NormalizeStick(gamepad.LeftThumbY);
                            int rightX = (int)NormalizeStick(gamepad.RightThumbX);
                            int rightY = (int)NormalizeStick(gamepad.RightThumbY);
                            int triggerL = RoundStick(gamepad.LeftTrigger, 75f);
                            int triggerR = RoundStick(gamepad.RightTrigger, 75f);

                            WriteData(stream, $"te-jstk,{leftX},{leftY},{rightX},{rightY},{triggerL},{triggerR};");

                            Thread.Sleep(25);

                            // Send button data
                            foreach (var button in Enum.GetValues(typeof(GamepadButtonFlags)).Cast<GamepadButtonFlags>())
                            {
                                if (gamepad.Buttons.HasFlag(button) && !pressedButtons.Contains(button))
                                {
                                    pressedButtons.Add(button);
                                    WriteData(stream, $"te-btn,{button};");
                                }
                                else if (!gamepad.Buttons.HasFlag(button) && pressedButtons.Contains(button))
                                {
                                    pressedButtons.Remove(button);
                                    WriteData(stream, $"te-btn,-{button};");
                                }
                            }
                        }
                        else
                        {
                            const int axisFull = 100; // matches NormalizeStick range used elsewhere
                            int lx = pressedKeys.Contains(Keys.D) ? axisFull : pressedKeys.Contains(Keys.A) ? -axisFull : 0;
                            int ly = pressedKeys.Contains(Keys.W) ? axisFull : pressedKeys.Contains(Keys.S) ? -axisFull : 0;

                            int rx = pressedKeys.Contains(Keys.Right) ? axisFull : pressedKeys.Contains(Keys.Left) ? -axisFull : 0;
                            int ry = pressedKeys.Contains(Keys.Up) ? axisFull : pressedKeys.Contains(Keys.Down) ? -axisFull : 0;

                            int tL = pressedKeys.Contains(Keys.Q) ? 1 : 0;
                            int tR = pressedKeys.Contains(Keys.E) ? 1 : 0;

                            WriteData(stream, $"te-jstk,{lx},{ly},{rx},{ry},{tL},{tR};");

                            foreach (KeyValuePair<Keys, string> pair in keyMap) {
                                if (pressedKeys.Contains(pair.Key) && !sentKeys.Contains(pair.Key)) {
                                    sentKeys.Add(pair.Key);
                                    WriteData(stream, $"te-btn,{pair.Value};");
                                }
                                else if (!pressedKeys.Contains(pair.Key) && sentKeys.Contains(pair.Key)) {
                                    sentKeys.Remove(pair.Key);
                                    WriteData(stream, $"te-btn,-{pair.Value};");
                                }
                            }

                        }
                    } else
                    {
                        if (sentKeys.Count > 0)
                        {
                            sentKeys.Clear();
                        }
                    }
                    
                    Thread.Sleep(25);
                }
            }
            catch (Exception ex)
            {
                WriteConsole($"[SOCKET] Error: {ex.Message}", Color.Red);
            }
            finally
            {
                client.Close();
                WriteConsole("[SOCKET] Connection closed.", Color.DeepPink);
            }
            sessionActive = false;
            socketConnected = false;
            disconnectControlModifications.Apply();
            ss_robot.Text = "Robot disconnected.";
            ss_robot.BackColor = Color.Red;
            socketThread = null;
        }

        private void RunSocketReciever(NetworkStream stream)
        {
            try
            {
                WriteConsole($"[RECIEVER] Connected.", Color.DeepPink);
                while (socketConnected)
                {
                    byte[] buffer = new byte[256];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                    if (message.StartsWith("[STATE] "))
                    {
                        string stateMessage = message[8..];
                        if (stateMessage == "DISABLE")
                        {
                            new ThreadSafeModification<TextBox>(robotState, (c) =>
                            {
                                c.Text = robotSimulated ? "SIM - DISABLED" : "DISABLED";
                                c.BackColor = Color.Red;
                                ms_auton.Enabled = true;
                            }).Apply();
                            isInTele = false;
                            SetEnableButton(false);
                        }
                        else if (stateMessage == "TELEOP")
                        {
                            if (pressedKeys.Count > 0)
                            {
                                pressedKeys.Clear();
                            }
                            new ThreadSafeModification<TextBox>(robotState, (c) =>
                            {
                                c.Text = robotSimulated ? "SIM - TELEOP" : "TELEOP";
                                c.BackColor = Color.Green;
                                ms_auton.Enabled = false;
                            }).Apply();
                            isInTele = true;
                            SetEnableButton(true);
                        }
                        else if (stateMessage == "AUTONOMOUS")
                        {
                            new ThreadSafeModification<TextBox>(robotState, (c) =>
                            {
                                c.Text = robotSimulated ? "SIM - AUTONOMOUS" : "AUTONOMOUS";
                                c.BackColor = Color.Yellow;
                                ms_auton.Enabled = false;
                            }).Apply();
                            isInTele = false;
                            SetEnableButton(true);
                        }
                        continue;
                    }
                    else if (message.StartsWith("[ROBOTINFO]"))
                    {
                        try
                        {
                            var splitted = message.Split("[SIG]");
                            if (splitted.Length >= 1)
                            {
                                if (splitted.Length >= 1 && splitted[1].Contains("[AUTONS]"))
                                {
                                    var autonList = splitted[1].Replace("[AUTONS]", "").Split(',');
                                    foreach (var item in autonList)
                                    {
                                        if (item == "")
                                        {
                                            continue;
                                        }
                                        new ThreadSafeModification<RichTextBox>(console, (c) =>
                                        {
                                            robot_auton.Items.Add(item);
                                        }).Apply();
                                    }
                                    splitted[1] = "";
                                }
                                if (splitted.Length >= 2 && splitted[2].Contains("[FLAGS]"))
                                {
                                    var flags = splitted[2].Replace("[FLAGS]", "").Split(',');
                                    foreach (var item in flags)
                                    {
                                        if (item == "")
                                        {
                                            continue;
                                        }
                                        if (item == "sim")
                                        {
                                            robotSimulated = true;
                                        }
                                        else if (item == "camera" && !robotSimulated)
                                        {
                                            new ThreadSafeModification<Panel>(p_main, (c) =>
                                            {
                                                ms_cams.Enabled = true;
                                            }).Apply();
                                        }
                                    }
                                    splitted[2] = "";
                                }
                            }
                            else
                            {
                                throw new Exception("No [AUTONS] found in message.");
                            }
                            WriteRobotInfo($"{splitted[0].Replace("\n", "\r\n").Replace("[ROBOTINFO]", "")}");
                        }
                        catch (Exception ex)
                        {
                            WriteConsole($"[RECIEVER] Error: {ex.Message}", Color.Red);
                            WriteRobotInfo($"{message[12..].Replace("\n", "\r\n")}");
                        }
                    }
                    else
                    {
                        foreach (string line in message.Split("%NL%"))
                        {
                            string toConsole = line.Replace("%NL%", "");

                            Color color = defaultConsoleColor;

                            if (line.StartsWith("%GREEN%"))
                            {
                                toConsole = toConsole.Replace("%GREEN%", "");
                                color = Color.LawnGreen;
                            }
                            else if (toConsole.StartsWith("%ORANGE%"))
                            {
                                toConsole = toConsole.Replace("%ORANGE%", "");
                                color = Color.Orange;
                            }
                            else if (toConsole.StartsWith("%BLUE%"))
                            {
                                toConsole = toConsole.Replace("%BLUE%", "");
                                color = Color.Blue;
                            }
                            else if (toConsole.StartsWith("%YELLOW%"))
                            {
                                toConsole = toConsole.Replace("%YELLOW%", "");
                                color = Color.Yellow;
                            }
                            else if (toConsole.StartsWith("%RED%"))
                            {
                                toConsole = toConsole.Replace("%RED%", "");
                                color = Color.Red;
                            }
                            WriteConsole(toConsole, color);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteConsole($"[RECIEVER] Reciever Error: {ex.Message}", Color.Red);
            }
            finally
            {
                socketReciever = null;
                WriteConsole("[RECIEVER] Reciever closed.", Color.DeepPink);
            }
        }

        private static float NormalizeStick(short value)
        {
            return Math.Clamp(value / 327.67f, -100f, 100f);
        }

        private static int RoundStick(float value, float threshold = 0.55f)
        {
            if (value > threshold) return 1;
            if (value < -threshold) return -1;
            return 0;
        }

        private static void WriteData(NetworkStream stream, string message)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(message + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }

        private void WriteConsole(string message, Color color)
        {
            if (message == "")
            {
                return;
            }
            new ThreadSafeModification<RichTextBox>(console, (c) =>
            {
                try
                {
                    c.SelectionStart = c.Text.Length; // Move to the end of the text
                    c.SelectionLength = 0; // Ensure no text is selected
                    c.SelectionColor = color; // Set the selection color to orange
                    c.AppendText(message + "\r\n"); // Append the message
                    c.SelectionStart = c.Text.Length; // Move to the end of the text again
                    c.SelectionColor = c.ForeColor; // Reset the selection color to default
                    c.ScrollToCaret(); // Scroll to the caret
                }
                catch { }
            }).Apply();
        }
        private void WriteConsole(string message)
        {
            WriteConsole(message, defaultConsoleColor);
        }

        private void WriteRobotInfo(string message)
        {
            new ThreadSafeModification<TextBox>(robotInfo, (c) =>
            {
                c.AppendText(message + "\r\n");
            }).Apply();
        }

        private void SetEnableButton(bool enable)
        {
            new TSMCollection([
                new ThreadSafeModification<Button>(b_teleop, (c) =>
                {
                    c.BackColor = isInTele ? Color.LightGreen : Color.FromKnownColor(KnownColor.Control);
                    c.Enabled = !isInTele;
                    if (enable)
                    {
                        ActiveControl = b_disable;
                    }
                }),
                new ThreadSafeModification<Button>(b_auton, (c) =>
                {
                    c.BackColor = enable && !isInTele ? Color.Khaki : Color.FromKnownColor(KnownColor.Control);
                    c.Enabled = !enable || isInTele;
                    if (enable)
                    {
                        ActiveControl = b_disable;
                    }
                }),
                new ThreadSafeModification<Button>(b_disable, (c) =>
                {
                    c.BackColor = enable ? Color.FromKnownColor(KnownColor.Control) : Color.Tomato;
                    c.Enabled = enable;
                    if (!enable)
                    {
                        ActiveControl = c;
                    }
                })
            ]).Apply();
        }

        private void ConnectButtonClicked(object sender, EventArgs e)
        {
            if (cb_hostname.Text == "" || port < nud_port.Minimum || port > nud_port.Maximum)
            {
                MessageBox.Show("Please enter a valid hostname and port.", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Error);
                sessionActive = false;
                return;
            }

            sessionActive = !sessionActive;
            hostName = cb_hostname.Text;
            port = (int)nud_port.Value;
            cb_hostname.Enabled = !sessionActive;
            nud_port.Enabled = !sessionActive;
            if (sessionActive)
            {
                ss_controller_Click(null, null);

                b_connect.Text = "Disconnect";
                console.Text = "";
                robotInfo.Text = "";
                robotState.Text = "Connecting";
                robotState.BackColor = Color.SandyBrown;
                robotSimulated = false;
                b_startCode.Visible = false;
                b_connect.Enabled = false;
                b_teleop.Enabled = false;
                b_teleop.BackColor = Color.FromKnownColor(KnownColor.Control);
                b_auton.Enabled = false;
                b_auton.BackColor = Color.FromKnownColor(KnownColor.Control);
                b_disable.Enabled = false;
                b_disable.BackColor = Color.FromKnownColor(KnownColor.Control);

                if (!cb_hostname.Items.Contains(hostName))
                {
                    cb_hostname.Items.Add(hostName);
                }

                MainSettings.Default.Hostname = hostName;
                MainSettings.Default.Port = port;
                MainSettings.Default.Save();

                socketThread = new Thread(RunSocketThread)
                {
                    IsBackground = true
                };
                socketThread.Start();
            }
            else
            {
                socketConnected = false;
                b_connect.Text = "&Connect to Robot";
            }
        }
        private void clearConsole_Click(object sender, EventArgs e)
        {
            console.Text = "";
        }

        private void AddConsoleText(string message, Color color)
        {
            console.SelectionStart = console.Text.Length; // Move to the end of the text
            console.SelectionLength = 0; // Ensure no text is selected
            console.SelectionColor = color; // Set the selection color to orange
            console.AppendText(message + "\r\n"); // Append the message
            console.SelectionStart = console.Text.Length; // Move to the end of the text again
            console.SelectionColor = console.ForeColor; // Reset the selection color to default
            console.ScrollToCaret(); // Scroll to the caret
        }

        private void help_about_Click(object sender, EventArgs e)
        {
            MessageBox.Show($"Written by Kyle Rush.\nVersion {VersionStr}", "About SPRK Controller\nIcon from Freepik", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ss_controller_Click(object? sender, EventArgs? e)
        {
            if (joystick_bypass.Checked)
            {
                if (e != null) // if this click came from a real control
                {
                    bypassJoystick = false;
                    joystick_bypass.Checked = false;
                }
                else // respect the bypass
                {
                    return;
                }
            }
            controller = new Controller(UserIndex.One);
            ss_controller.Image = Properties.Resources.ICO_Joystick;
            if (controller.IsConnected)
            {
                ss_controller.Text = "Joystick Connected.";
                ss_controller.BackColor = Color.Green;
            }
            else
            {
                ss_controller.Text = "Joystick Disconnected.";
                ss_controller.BackColor = Color.Red;
                AddConsoleText("[SPRK CONTROLLER] WARNING! No joystick detected.\r\nPress F1 to rescan. Alternatively, use keyboard input with Ctrl+B", Color.Orange);
            }
        }

        private void WIN_MAIN_FormClosing(object sender, FormClosingEventArgs e)
        {
            sessionActive = false;
            socketConnected = false;
        }

        private void robotState_Enter(object sender, EventArgs e)
        {
            ActiveControl = null;
        }

        private void b_teleop_Click(object sender, EventArgs e)
        {
            teleopRequest = true;
        }

        private void b_auton_Click(object sender, EventArgs e)
        {
            autoRequest = true;
        }

        private void b_disable_Click(object sender, EventArgs e)
        {
            disableRequest = true;
        }
        private void b_kill_Click(object sender, EventArgs e)
        {
            killRequest = true;
        }

        private void WIN_MAIN_KeyDown(object sender, KeyEventArgs e)
        {
            pressedKeys.Add(e.KeyCode);

            if (e.KeyCode == Keys.Enter)
            {
                disableRequest = true;
                e.Handled = true;
            }

            else if (e.KeyCode == Keys.Space)
            {
                //e.Handled = true;
            }

            if (bypassJoystick && isInTele)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            if (b_teleop.Enabled && pressedKeys.Contains(Keys.OemPipe) && pressedKeys.Contains(Keys.Oem6) && pressedKeys.Contains(Keys.Oem4))
            {
                b_teleop.PerformClick();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void WIN_MAIN_KeyUp(object sender, KeyEventArgs e)
        {
            pressedKeys.Remove(e.KeyCode);
        }

        private void b_startCode_Click(object sender, EventArgs e)
        {
            if (cb_hostname.Text.ToLower() != "sprk")
            {
                MessageBox.Show("This is only supported on the offical S.P.R.K.!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Process sshProcess = Process.Start("cmd.exe");
            sshProcess.StartInfo.Arguments = $"/c ssh pi@{cb_hostname.Text} \"cd /home/pi/robot && python3 Main.py\"";
            sshProcess.Start();
        }

        private void robot_auton_TextUpdate(object sender, EventArgs e)
        {
            selectedAutonName = robot_auton.Text;
            changeAutoRequest = true;
        }

        private void joystick_bypass_Click(object? sender, EventArgs? e)
        {
            bypassJoystick = joystick_bypass.Checked;

            ss_controller.BackColor = Color.MediumAquamarine;

            if (bypassJoystick)
            {
                ss_controller.Image = Properties.Resources.ICO_KeyboardKey;
                ss_controller.Text = "Keyboard Input";
            }
            else
            {
                ss_controller.Image = Properties.Resources.ICO_Joystick;
                ss_controller.Text = "Joystick Connected.";
                ss_controller_Click(null, null);
            }
        }

        private void backgroundPrefs_CheckedChanged(object sender, EventArgs e)
        {
            p_main.BackgroundImage = backgroundPrefs.Checked ? GetImageFromBytes(Properties.Resources.Background) : null;
            MainSettings.Default.UsingBackground = backgroundPrefs.Checked;
            MainSettings.Default.Save();
        }

        private static Image GetImageFromBytes(byte[] bytes)
        {
            return Image.FromStream(new MemoryStream(bytes));
        }

        private void vision_launch_Click(object sender, EventArgs e)
        {
            cameraStartRequest = true;
            stream_launch.Enabled = false;
            new WIN_CameraStream($"{hostName} - Camera Stream", $"http://{hostName}:{MainSettings.Default.CameraStreamPort}/?action=stream", () =>
            {
                stream_launch.Enabled = true;
            }).Show();
        }

        private void help_more_Click(object sender, EventArgs e)
        {
            ProcessStartInfo psInfo = new()
            {
                FileName = Properties.Resources.ProjectWebsite,
                UseShellExecute = true,
            };
            Process.Start(psInfo);
        }
    }

}
