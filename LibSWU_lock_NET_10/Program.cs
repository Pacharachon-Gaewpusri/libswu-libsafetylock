
using LibSWU_lock_NET_10;
using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
//using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;



namespace SecureAuthApp
{

    // ==========================================
    // 1. APPLICATION ENTRY POINT
    // ==========================================
    static class Program
    {


        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Starts the application with the lockout form
            Application.Run(new LockoutAuthForm());
        }
    }
    // ==========================================
    // 2. REMAINING TIME CHECKER FORM
    // ==========================================
    public class RemainingTimeChecker : Form
    {
        private Label lblTimerDisplay;
        private System.Windows.Forms.Timer _displayTimer;
        private Func<DateTime> _getTargetTime;

        public RemainingTimeChecker(Func<DateTime> getTargetTime)
        {
            _getTargetTime = getTargetTime;

            // Form Properties
            this.Text = "Session Time Remaining:";
            this.Size = new Size(400, 240);
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true; // Keeps window floating above active applications

            // Timer Label
            lblTimerDisplay = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 16, FontStyle.Bold),
                ForeColor = Color.DarkBlue
            };
            this.Controls.Add(lblTimerDisplay);

            // 1-Second UI Refresh Timer
            _displayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _displayTimer.Tick += DisplayTimer_Tick;
            _displayTimer.Start();

            UpdateCountdownText();
        }

        private void DisplayTimer_Tick(object sender, EventArgs e)
        {
            UpdateCountdownText();
        }

        private void UpdateCountdownText()
        {
            DateTime target = _getTargetTime();
            TimeSpan remaining = target - DateTime.Now;

            if (remaining.TotalSeconds <= 0)
            {
                lblTimerDisplay.Text = "Relocking System...";
                lblTimerDisplay.ForeColor = Color.Red;
            }
            else
            {
                lblTimerDisplay.Text = $"Relock in: {remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }

        //protected override void OnFormClosing(FormClosingEventArgs e)
        //{
        //    _displayTimer?.Stop();
        //    _displayTimer?.Dispose();
        //    base.OnFormClosing(e);
        //}
    }
    // ==========================================
    // 3. LOCKOUT FORM IMPLEMENTATION
    // ==========================================
    public class LockoutAuthForm : Form
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private Label UsernameLabel;
        private Label PasswordLabel;
        private Button btnLogin;
        private Button CheckTimeRemainingbtn; // Unclickable button for displaying remaining time
        private Button ConfirmLogoutBtn;
        private Button SWUbtn;
        private Image SWUicon;
        private Image LogoutIcon;
        private NotifyIcon TrayNotiIcon;
        private ContextMenuStrip trayContextMenu;
        private Label lblError;
        private System.Windows.Forms.Timer _relockTimer;
        private System.Windows.Forms.Timer _warningTimer; // Timer for 1-minute warning
        private System.Windows.Forms.Timer _countdownTimer; // 1-second tick timer for UI countdown
        private DateTime _relockTargetTime; // Stores precise end time to prevent timer drift
        private bool _isStandbyMode = false;// Tracks current active layout state
        // Store original bounds (design-time) for each child control so we can scale from them
        
        private RemainingTimeChecker _timeCheckerForm; // Instance reference for the popup checker window
        private readonly Dictionary<Control, Rectangle> _originalBounds = new();

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;


        private (string Username, string Password) postData = ("admin", "exampleP@ssword"); // adjust values



        public LockoutAuthForm()
        {

            // Ensure DPI scaling behavior
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.StartPosition = FormStartPosition.CenterScreen;

            //ApplyLockdownSettings();
            InitializeRelockTimer();
            InitializeFormComponents();
            
            // Prevent user from closing window during active lockdown
            this.FormClosing += (s, e) => {
                if (e.CloseReason == CloseReason.UserClosing)
                    e.Cancel = true;
            };
            //Debug.WriteLine($"Form size: {ClientSize.Width}x{ClientSize.Height}");


        }
        private static string GetLocalIPv4Address()
        {
            try
            {
                // Establishes a socket to determine the active local network interface IPv4 address
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch
            {
                // Fallback to local hostname resolution if no active socket connection is found
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            return "127.0.0.1";
        }
        private void InitializeFormComponents()
        {

            this.txtUsername = new TextBox { AutoSize = true };
            this.txtPassword = new TextBox { PasswordChar = '*', AutoSize = true };

            // Place labels to the left of textboxes
            UsernameLabel = new Label { Text = "User:", AutoSize = true };
            PasswordLabel = new Label { Text = "Password:", AutoSize = true };

            SWUicon = Image.FromFile("C:\\Users\\Library\\source\\repos\\libswu-libsafetylock\\LibSWU_lock_NET_10\\Resources\\SWUicon_resized.png");
            LogoutIcon = Image.FromFile("C:\\Users\\Library\\source\\repos\\libswu-libsafetylock\\LibSWU_lock_NET_10\\Resources\\logout.png");

            btnLogin = new Button { Text = "Login", AutoSize = true };
            SWUbtn = new Button { Image = LogoutIcon, Text = "Logout", AutoSize = true, Visible = false, Enabled = true };
            ConfirmLogoutBtn = new Button { Text = "Confirm Logout?", AutoSize = true, Visible = false, BackColor = Color.LightGray };

            CheckTimeRemainingbtn = new Button{Text = "Check Time Remaining:", AutoSize = true, Visible = false, BackColor = Color.LightGray};

            AcceptButton = this.btnLogin;

            // Pressing Enter will trigger the login button
            btnLogin.Click += BtnLogin_Click;
            // Add MouseDown for right-click detection
            SWUbtn.MouseDown += SWUbtn_RightClick;
            CheckTimeRemainingbtn.MouseClick += CheckTimeRemainingbtn_Click;
            // Bind the confirmation button's click event so it functions when visible
            ConfirmLogoutBtn.Click += ConfirmLogoutBtn_Click;

            // Triggers login specifically when Enter is pressed inside the password textbox
            this.txtPassword.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true; // Prevents the Windows error "ding" sound
                    this.btnLogin.PerformClick();

                }
            };

            this.lblError = new Label { AutoSize = true, ForeColor = System.Drawing.Color.Red };

            // 1. Configure System Tray Icon & Context Menu
            trayContextMenu = new ContextMenuStrip();
            ToolStripMenuItem confirmLogoutMenuItem = new ToolStripMenuItem("Confirm Logout?", null, ConfirmLogoutBtn_Click);
            ToolStripMenuItem checkTimeRemainingMenuItem = new ToolStripMenuItem("Check Time Remaining", null, CheckTimeRemainingbtn_Click);
            trayContextMenu.Items.Add(confirmLogoutMenuItem);
            trayContextMenu.Items.Add(checkTimeRemainingMenuItem);

            // Add textboxes/buttons first so we can position labels relative to them
            this.Controls.Add(txtUsername);
            this.Controls.Add(txtPassword);
            this.Controls.Add(UsernameLabel);
            this.Controls.Add(PasswordLabel);

            this.Controls.Add(btnLogin);
            this.Controls.Add(lblError);


            this.Controls.Add(SWUbtn);
            this.Controls.Add(CheckTimeRemainingbtn); // Added countdown button to controls
            this.Controls.Add(ConfirmLogoutBtn);

            // Hook up resize and load events to keep panel centered
            this.Load += LockoutAuthForm_Load;
            this.Resize += LockoutAuthForm_Resize;

            // Apply initial layout positioning
            AuthenticationLayout();

        }

        private void AuthenticationLayout()
        {
            _isStandbyMode = false;

            // Restore solid background & disable transparency
            this.TransparencyKey = Color.Empty;
            this.BackColor = SystemColors.Control;

            // Query live window dimensions on demand
            int tabWidth = ClientSize.Width;
            int tabHeight = ClientSize.Height;

            if (txtUsername == null || txtPassword == null) return;

            // Toggle visibilities for authentication mode
            txtUsername.Visible = true;
            txtPassword.Visible = true;
            UsernameLabel.Visible = true;
            PasswordLabel.Visible = true;
            btnLogin.Visible = true;
            lblError.Visible = true;

            SWUbtn.Visible = false;
            ConfirmLogoutBtn.Visible = false;

            // Set dimensions and center textboxes relative to current window height & width
            txtUsername.Width = 160;
            txtUsername.Height = 20;
            txtUsername.Location = new Point((tabWidth - txtUsername.Width) / 2, (tabHeight / 2) - txtUsername.Height - 5);

            txtPassword.Width = 160;
            txtPassword.Height = 20;
            txtPassword.Location = new Point((tabWidth - txtUsername.Width) / 2, (tabHeight / 2) + 5);

            // Align labels directly to the left of textboxes
            UsernameLabel.Location = new Point(txtUsername.Left - PasswordLabel.PreferredWidth, txtUsername.Top);
            PasswordLabel.Location = new Point(txtPassword.Left - PasswordLabel.PreferredWidth, txtPassword.Top);

            // Center button and error message vertically under inputs
            btnLogin.Width = 75;
            btnLogin.Height = 25;
            btnLogin.Location = new Point((tabWidth - btnLogin.Width) / 2, txtPassword.Bottom + 15);

            lblError.Location = new Point((tabWidth - lblError.PreferredWidth) / 2, btnLogin.Bottom + 10);

            this.BringToFront();
            this.Activate();
        }
        private void StandbyLayout()
        {
            _isStandbyMode = true;

            int tabWidth = ClientSize.Width;
            int tabHeight = ClientSize.Height;

            this.ShowInTaskbar = false;

            TrayNotiIcon = new NotifyIcon
            {
                Icon = Icon.FromHandle(((Bitmap)SWUicon).GetHicon()), // Sets system icon; update with custom icon if available
                Text = "Secure Auth App",
                ContextMenuStrip = trayContextMenu, // Assign right-click menu
                Visible = false
            };

            // Hide authentication controls
            txtUsername.Visible = false;
            txtPassword.Visible = false;
            UsernameLabel.Visible = false;
            PasswordLabel.Visible = false;
            btnLogin.Visible = false;
            lblError.Visible = false;

            // Show standby controls
            TrayNotiIcon.Visible = true;

            // Make unclickable countdown button visible and position it
            CheckTimeRemainingbtn.Visible = false;
            CheckTimeRemainingbtn.Location = new Point((tabWidth - ConfirmLogoutBtn.Width), SWUbtn.Top - (2 * ConfirmLogoutBtn.Height));

            ConfirmLogoutBtn.Visible = false; // Remains hidden until right-click
            ConfirmLogoutBtn.Width = 120;
            ConfirmLogoutBtn.Height = 25;
            ConfirmLogoutBtn.Location = new Point((tabWidth - ConfirmLogoutBtn.Width), SWUbtn.Top - ConfirmLogoutBtn.Height);

            //// Add MouseDown for right-click detection
            TrayNotiIcon.MouseDown += SWUbtn_RightClick;
            // Bind the confirmation button's click event so it functions when visible
            ConfirmLogoutBtn.Click += ConfirmLogoutBtn_Click;
            CheckTimeRemainingbtn.MouseClick += CheckTimeRemainingbtn_Click;

            InitializeRelockTimer();

        }
        private void CountdownTimer()
        {
            TimeSpan remainingTime = _relockTargetTime - DateTime.Now;
            if (remainingTime.TotalSeconds <= 0)
            {
                // Time's up, reset countdown for next cycle
                StartSessionTimers();
                remainingTime = TimeSpan.FromMinutes(2); // Reset to 2 minutes
            }
            // Update the countdown display on the unclickable button
        }
        private void LockoutAuthForm_Resize(object sender, EventArgs e)
        {
            // Recalculate control placement whenever window size or state changes
            AuthenticationLayout();
            // Logs the current window state along with client dimensions (usable area)
            System.Diagnostics.Debug.WriteLine(
                $"[Window Resized] State: {this.WindowState} | Width: {this.ClientSize.Width}px, Height: {this.ClientSize.Height}px"
            );
            Debug.WriteLine($"Login Button size: {btnLogin.Width}x{btnLogin.Height}");
            Debug.WriteLine($"Logout Button size: {SWUbtn.Width}x{SWUbtn.Height}");
            Debug.WriteLine($"Username Textbox Button size: {txtUsername.Width}x{txtUsername.Height}");
            Debug.WriteLine($"Password Textbox Button size: {txtPassword.Width}x{txtPassword.Height}");
            Debug.WriteLine($"Confirm Logout Button size: {ConfirmLogoutBtn.Width}x{ConfirmLogoutBtn.Height}");
        }
        private void PerformLogout()
        {
            // Stop all active timers
            StopSessionTimers();

            // Clear login input fields
            txtUsername.Clear();
            txtPassword.Clear();
            lblError.Text = "";

            // Hide system tray icon
            if (TrayNotiIcon != null)
            {
                TrayNotiIcon.Visible = false;
            }

            // Restore form visibility and re-apply lockdown settings
            this.BringToFront();
            this.Show();
            //ApplyLockdownSettings();

            // Re-hook low-level keyboard hooks to block shortcuts (Alt+Tab, Win Key)
            if (_hookID == IntPtr.Zero)
            {
                _hookID = SetHook(_proc);
            }

            // Switch back to authentication interface
            AuthenticationLayout();
        }
        private void InitializeRelockTimer()
        {
            _relockTimer = new System.Windows.Forms.Timer();
            _relockTimer.Interval = 2 * 60 * 1000; // 2 minutes in milliseconds
            _relockTimer.Tick += RelockTimer_Tick;


            // 1-minute warning timer
            _warningTimer = new System.Windows.Forms.Timer();
            _warningTimer.Interval = 1 * 60 * 1000; // 1 minute (60,000 ms)
            _warningTimer.Tick += WarningTimer_Tick;

            // Initialize 1-second countdown update timer
            _countdownTimer = new System.Windows.Forms.Timer();
            _countdownTimer.Interval = 1 * 1 * 1000; // 1000 ms = 1 second
            _countdownTimer.Tick += CountdownTimer_Tick;
        }
        private void StartSessionTimers()
        {
            _relockTargetTime = DateTime.Now.AddMinutes(2); // Set countdown goal (2 minutes from now)

            _relockTimer.Stop();
            _relockTimer.Start();

            _warningTimer.Stop();
            _warningTimer.Start();

            _countdownTimer.Stop();
            _countdownTimer.Start();
        }

        private void StopSessionTimers()
        {
            _relockTimer?.Stop();
            _warningTimer?.Stop();
            _countdownTimer?.Stop();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            CountdownTimer();
        }

        private void RelockTimer_Tick(object sender, EventArgs e)
        {
            // Reset countdown for next cycle
            StartSessionTimers();
        }

        private void WarningTimer_Tick(object sender, EventArgs e)
        {
            _warningTimer.Stop();
            MessageBox.Show("เวลาใกล้หมด กรุณาบันทึกงานของคุณ\n Time is almost up. Please save your work.");
        }
        private void OpenRemainingTimeChecker()
        {
            if (_timeCheckerForm == null || _timeCheckerForm.IsDisposed)
            {
                _timeCheckerForm = new RemainingTimeChecker(() => _relockTargetTime);
                _timeCheckerForm.Show();
            }
            else
            {
                _timeCheckerForm.BringToFront();
                _timeCheckerForm.Activate();
            }
        }
        private void CloseRemainingTimeChecker()
        {
            if (_timeCheckerForm != null && !_timeCheckerForm.IsDisposed)
            {
                _timeCheckerForm.Close();
                _timeCheckerForm.Dispose();
                _timeCheckerForm = null;
            }
        }
        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            var creds = (Username: txtUsername.Text, Password: txtPassword.Text);
            bool ok = await SendAuthRequest(creds);
            if (ok)
            {
                // Temporarily unhook keyboard lock so user can use the OS
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }

                // Clear credentials and hide screen
                txtUsername.Clear();
                txtPassword.Clear();
                lblError.Text = "";

                // Switch UI to Standby Layout and start session timers
                this.WindowState = FormWindowState.Minimized;
                StandbyLayout();

                // Start Timer
                InitializeRelockTimer();
                StartSessionTimers();
            }
            else
            {
                lblError.Text = "Invalid credentials.";
            }

        }

        private async void SWUbtn_RightClick(object sender, MouseEventArgs e)
        {
            if (e is MouseEventArgs mouseArgs && mouseArgs.Button == MouseButtons.Right)
            {
                ConfirmLogoutBtn.Visible = true;
                ConfirmLogoutBtn.Enabled = true;
                CheckTimeRemainingbtn.Visible = true;
                CheckTimeRemainingbtn.Enabled = true;
                ConfirmLogoutBtn.BringToFront();
                CheckTimeRemainingbtn.BringToFront();
            }
        }
        private void CheckTimeRemainingbtn_Click(object sender, EventArgs e)
        {
                OpenRemainingTimeChecker();
        }
        private async void ConfirmLogoutBtn_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Are you sure you want to logout?", "Confirm Logout", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                PerformLogout();
            }
        }
        private void ApplyLockdownSettings()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BringToFront();
            this.Activate();
        }
        private void LockoutAuthForm_Load(object sender, EventArgs e)
        {
            _hookID = SetHook(_proc);

        }

        //private void LockoutAuthForm_FormClosed(object sender, FormClosedEventArgs e)
        //{
        //    _relockTimer?.Stop();
        //    _relockTimer?.Dispose();

        //    if (_hookID != IntPtr.Zero)
        //    {
        //        UnhookWindowsHookEx(_hookID);
        //        _hookID = IntPtr.Zero;
        //    }
        //}

        private static readonly HttpClient client = new HttpClient();

        static async Task<bool> SendAuthRequest((string Username, string Password) credentials)
        {
            string requestUrl = "https://lib.swu.ac.th/api/auth";
            var postData = new
            {
                Username = credentials.Username,
                Password = credentials.Password,
                Action = "Cybercafe",
                ip = GetLocalIPv4Address()
            };

            HttpResponseMessage response = await client.PostAsJsonAsync(requestUrl, postData);
            return response.IsSuccessStatusCode;
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;
                if (key == Keys.LWin || key == Keys.RWin) return (IntPtr)1;
                if ((Control.ModifierKeys & Keys.Alt) != 0 && (key == Keys.Tab || key == Keys.Escape)) return (IntPtr)1;
                if ((Control.ModifierKeys & Keys.Control) != 0 && key == Keys.Escape) return (IntPtr)1;
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);


    }
}