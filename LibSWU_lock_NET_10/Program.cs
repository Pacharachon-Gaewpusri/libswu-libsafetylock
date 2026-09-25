
using LibSWU_lock_NET_10;
using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection.Metadata;

//using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using System.Xml.Linq;
using static SecureAuthApp.LockAuthForm;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;




namespace SecureAuthApp
{
 
    // ==========================================
    //  APPLICATION ENTRY POINT
    // ==========================================
    static class Program
    {


        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Starts the application with the lockout form
            Application.Run(new LockAuthForm());
            
        }
    }

    // ==========================================
    //  LOCKOUT FORM IMPLEMENTATION
    // ==========================================
    public class LockAuthForm : Form
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private Label UsernameLabel;
        private Label PasswordLabel;
        private Button btnLogin;
        private Button CheckTimeRemainingbtn; 
        private Button ConfirmLogoutBtn;
        private Button SWUbtn;
        private Image SWUicon;
        private Image LogoutIcon;
        private Image BG;
        private NotifyIcon TrayNotiIcon;
        private ContextMenuStrip trayContextMenu;
        private Label lblError;
        private System.Windows.Forms.Timer _relockTimer;
        private System.Windows.Forms.Timer _warningTimer; // Timer for 1-minute warning
        private System.Windows.Forms.Timer _countdownTimer; // 1-second tick timer for UI countdown
        private DateTime _relockTargetTime; // Stores precise end time to prevent timer drift
        private DateTime _warningTime; // Stores precise warning time
        private bool _isStandbyMode;// Tracks current active layout state
        public bool _isTabOpened;
        private readonly Dictionary<Control, Rectangle> _originalBounds = new();

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        // Instance reference for the popup checker window
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private (string Username, string Password) postData = ("admin", "exampleP@ssword"); // adjust values
        private Label lblTimerDisplay = null;
        private string _currentLoggedInUser = null; // Stores the currently logged-in user
        private static readonly Dictionary<string, DateTime> _blockedUsers = new(StringComparer.OrdinalIgnoreCase); // Thread-safe / case-insensitive dictionary storing users and their lockout end times
        //private System.Windows.Forms.Timer _displayTimer;
        //private Func<DateTime> _getTargetTime;


        public LockAuthForm()
        {

            // Ensure DPI scaling behavior
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.StartPosition = FormStartPosition.CenterScreen;

            ApplyLockdownSettings();
            InitializeRelockTimer();
            InitializeFormComponents();
            

            // Prevent user from closing window during active lockdown
            this.FormClosing += (s, e) => {
                if (e.CloseReason == CloseReason.UserClosing)
                    e.Cancel = true;
            };


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

            string exeDirectory = Application.StartupPath;
            Debug.WriteLine($"[Startup Path] Executable Directory: {exeDirectory}");

            string swuPath = Path.Combine(Application.StartupPath, "Resources", "SWUicon_resized.png");
            string bgPath = Path.Combine(Application.StartupPath, "Resources", "The_digital_library_wisdom_and_passion_of_SWU(4).png");

            if (File.Exists(swuPath)) { SWUicon = Image.FromFile(swuPath); }

            btnLogin = new Button { Text = "Login", AutoSize = true };
            SWUbtn = new Button { Image = LogoutIcon, Text = "Logout", AutoSize = true, Visible = false, Enabled = true };
            ConfirmLogoutBtn = new Button { Text = "Logout?", AutoSize = true, Visible = false };

            CheckTimeRemainingbtn = new Button{Text = "Check Time Remaining:", AutoSize = true, Visible = false};

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
            string bgPath = Path.Combine(Application.StartupPath, "Resources", "The_digital_library_wisdom_and_passion_of_SWU(4).png");
            Image BG = Image.FromFile(bgPath);
            if (File.Exists(bgPath))
            {
                this.BackgroundImage = BG;
                this.BackgroundImageLayout = ImageLayout.Stretch; // Scales image to fit full screen
            }
            _isStandbyMode = false;
            this.ShowInTaskbar = false;

            //// Preserve transparency and background image visibility
            //this.TransparencyKey = Color.Empty;

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
            Icon trayIcon = SystemIcons.Application;

            if (SWUicon is Bitmap bmp)
            {
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tempIcon = Icon.FromHandle(hIcon))
                    {
                        trayIcon = (Icon)tempIcon.Clone(); // Copy icon into managed memory
                    }
                }
                finally
                {
                    DestroyIcon(hIcon); // Immediately release unmanaged Windows GDI handle
                }
            }
            // Dispose old instance to prevent duplicate tray icons on relock/re-login
            TrayNotiIcon?.Dispose();

            TrayNotiIcon = new NotifyIcon
            {
                Icon = trayIcon,
                Text = "Secure Auth App",
                ContextMenuStrip = trayContextMenu,
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
        private void UpdateCountdownText()
        {

            DateTime target = DateTime.Now.AddMinutes(2); // 2 minutes from now
            TimeSpan remaining = target - DateTime.Now;

            if (remaining.TotalSeconds <= 0)
            {
                lblTimerDisplay.Text = "Relocking System...";
                lblTimerDisplay.ForeColor = Color.Red;
                CloseRemainingTimeChecker();
                PerformLogout();
            }
            else
            {
                lblTimerDisplay.Text = $"Time Remaining: {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }

        private void CountdownTimer()
        {
            TimeSpan remainingTime = _relockTargetTime - DateTime.Now;
            if (remainingTime.TotalSeconds <= 0)
            {
                // Time's up, reset countdown for next cycle
                StopSessionTimers();
                PerformLogout();

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
            // Apply 1-hour lockout to the user who just logged out or timed out
            if (!string.IsNullOrEmpty(_currentLoggedInUser))
            {
                _blockedUsers[_currentLoggedInUser] = DateTime.Now.AddMinutes(1);
                _currentLoggedInUser = null; // Reset current session user
            }

            // Stop all active timers
            StopSessionTimers();

            //_timeCheckerForm = null;
            // Close tracked timer form and any additional open secondary forms
            CloseRemainingTimeChecker();
            for (int i = Application.OpenForms.Count - 1; i >= 0; i--)
            {
                Form openForm = Application.OpenForms[i];
                if (openForm != this)
                {
                    openForm.Close();
                    openForm.Dispose();
                }
            }

            // Reset input fields and state flags
            _isStandbyMode = false;
            _isTabOpened = false;
            // Clear login input fields
            txtUsername.Clear();
            txtPassword.Clear();
            lblError.Text = "";

            // Hide system tray icon
            if (TrayNotiIcon != null)
            {
                TrayNotiIcon.Visible = false;
            }
            ConfirmLogoutBtn.Visible = false;
            CheckTimeRemainingbtn.Visible = false;

            // Re-hook low-level keyboard hooks to block shortcuts (Alt+Tab, Win Key)
            if (_hookID == IntPtr.Zero)
            {
                _hookID = SetHook(_proc);
            }

            this.WindowState = FormWindowState.Normal;
            ApplyLockdownSettings(); // Restore form visibility and re-apply lockdown settings

            // Switch back to authentication interface
            AuthenticationLayout();
            this.BringToFront();
            this.Activate();
            this.Show();

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
            _warningTime = DateTime.Now.AddMinutes(1); // Set warning time (1 minute from now)

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
            _relockTimer.Start();
            _warningTimer.Start();
            _countdownTimer.Start();
        }

        public class YesNoMessageBox : Form
        {
            public YesNoMessageBox(string message, string title, Action onYesClicked)
            {
                this.Text = title;
                this.Size = new Size(320, 160);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.StartPosition = FormStartPosition.CenterScreen;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                this.TopMost = true;

                Label lblMessage = new Label
                {
                    Text = message,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = new Padding(10)
                };
                // Container panel to place Yes and No buttons horizontally
                Panel buttonPanel = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 45
                };
                Button btnYes = new Button
                {
                    Text = "Yes",
                    Size = new Size(80, 30),
                    Location = new Point(70, 8),
                    DialogResult = DialogResult.Yes
                };
                Button btnNo = new Button
                {
                    Text = "No",
                    Size = new Size(80, 30),
                    Location = new Point(160, 8),
                    DialogResult = DialogResult.No
                };

                btnYes.Click += (s, e) => {
                    this.Close();
                    onYesClicked?.Invoke();
                };
                btnNo.Click += (s, e) => this.Close();
                
                buttonPanel.Controls.Add(btnYes);
                buttonPanel.Controls.Add(btnNo);

                this.Controls.Add(lblMessage);
                this.Controls.Add(buttonPanel);
            }
            
        }
         public class WarningMessageBox : Form
        {
            public WarningMessageBox(string message, string title)
            {
                this.Text = title;
                this.Size = new Size(320, 160);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.StartPosition = FormStartPosition.CenterScreen;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                this.TopMost = true;

                Label lblMessage = new Label
                {
                    Text = message,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = new Padding(10)
                };
                Panel buttonPanel = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 45
                };
                Button btnOk = new Button
                {
                    Text = "OK",
                    Size = new Size(80, 30),
                    Location = new Point(120, 10),
                    DialogResult = DialogResult.OK
                };
                btnOk.Click += (s, e) => this.Close();

                buttonPanel.Controls.Add(btnOk);
                this.Controls.Add(lblMessage);
                this.Controls.Add(buttonPanel);
            }
            
        }
        //ConfirmLogoutMessageBox
        private WarningMessageBox _warningMsgTimer;
        private void WarningTimer_Tick(object sender, EventArgs e)
        {
            _warningTimer.Stop();
            _warningMsgTimer = null;
            if (_warningMsgTimer == null || _warningMsgTimer.IsDisposed)
            {
                _warningMsgTimer = new WarningMessageBox("เวลาใกล้หมด กรุณาบันทึกงานของคุณ\n Time is almost up. Please save your work.", "Warning");
                _warningMsgTimer.FormClosed += (s, e) => _isTabOpened = false;
                _warningMsgTimer.Show();
                _isTabOpened = true;
            }
            else
            {
                // Bring existing window to the front if clicked again
                _warningMsgTimer.BringToFront();
                _warningMsgTimer.Focus();
            }
            //MessageBox.Show("เวลาใกล้หมด กรุณาบันทึกงานของคุณ\n Time is almost up. Please save your work.");
        }
        private void DisplayTimer_Tick(object sender, EventArgs e)
        {
            UpdateCountdownText();
        }
        private TimeCheckerForm _timeCheckerWindow;

        private void OpenRemainingTimeChecker()
        {
            // Open a new window if one doesn't exist or was previously closed
           if (_timeCheckerWindow == null || _timeCheckerWindow.IsDisposed)
            {
                _timeCheckerWindow = new TimeCheckerForm(() => _relockTargetTime);
                _timeCheckerWindow.FormClosed += (s, e) => _isTabOpened = false;
                _timeCheckerWindow.Show();
                _isTabOpened = true;
            }
           else
            {
                 // Bring existing window to the front if clicked again
                 _timeCheckerWindow.BringToFront();
                 _timeCheckerWindow.Focus();
            }
        }

        private void CloseRemainingTimeChecker()
        {
            if (_timeCheckerWindow != null && !_timeCheckerWindow.IsDisposed)
            {
                _timeCheckerWindow.Close();
                _timeCheckerWindow = null;
            }
        }
        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            string inputUsername = txtUsername.Text.Trim();
            // 1. Check if user is currently timed out
            if (_blockedUsers.TryGetValue(inputUsername, out DateTime unblockTime))
            {
                if (inputUsername != "sc661010569") { //special tester privilege
                    if (DateTime.Now < unblockTime)
                    {
                        TimeSpan remaining = unblockTime - DateTime.Now;
                        WarningMessageBox blockWarning = new WarningMessageBox(
                            $"This account is on a 1-minute cooldown.\nPlease wait {remaining.Seconds} sec before logging in again.",
                            "Login Timeout Active"
                        );
                        blockWarning.ShowDialog();
                        return; // Block login attempt
                    }
                    else
                    {
                        _blockedUsers.Remove(inputUsername); // Lockout expired, clean up entry
                    }
                }
            }
            try
            {
                var creds = (Username: txtUsername.Text, Password: txtPassword.Text);
                var result = await SendAuthRequest(creds);

                if (result.IsSuccess)
                {
                    // 2. Save current username for the logout lockout trigger
                    _currentLoggedInUser = inputUsername;

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
                    this.Hide();
                    StandbyLayout();

                    // Start Timer
                    InitializeRelockTimer();
                    StartSessionTimers();
                }
                else if (result.StatusCode == HttpStatusCode.Unauthorized || result.StatusCode == HttpStatusCode.BadRequest)
                {
                    // Handles invalid credentials (400 / 401)
                    WarningMessageBox authWarning = new WarningMessageBox(
                        "Invalid username or password.\nPlease try again.",
                        "Login Warning"
                    );
                    authWarning.ShowDialog();
                    Debug.WriteLine($"[Authentication Error] HTTP Status Code: {(int)result.StatusCode} ({result.StatusCode}) | Message: {result.ErrorMessage}");
                }
                else
                {
                    // Explicitly throw an exception for non-credential API failures (500, 503, etc.)
                    throw new HttpRequestException(
                        string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Server returned an unexpected response state."
                            : result.ErrorMessage,
                        null,
                        result.StatusCode
                    );
                }
            }
            catch (Exception ex)
            {
                // Catches thrown API errors, network drops, and unexpected socket/HTTP exceptions
                HttpStatusCode statusCode = HttpStatusCode.InternalServerError;
                string issueDetails = ex.Message;

                // Extracts exact status code if the exception was triggered by HttpRequestException
                if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
                {
                    statusCode = httpEx.StatusCode.Value;
                }
                else if (ex is SocketException)
                {
                    statusCode = HttpStatusCode.ServiceUnavailable;
                }

                string apiMessage = $"The API encountered an issue.\n\nIssue: {issueDetails}\nHTTP Status Code: {(int)statusCode} ({statusCode})";

                WarningMessageBox apiWarning = new WarningMessageBox(apiMessage, "API Warning");
                apiWarning.ShowDialog();
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
        private YesNoMessageBox _LogoutTab;
        private void ConfirmLogoutBtn_Click(object sender, EventArgs e)
        {
            var LogoutTab = new YesNoMessageBox("คุณต้องการออกจากระบบหรือไม่?\nDo you want to log out?", "Confirm Logout",() => PerformLogout());
            if (_LogoutTab == null || _LogoutTab.IsDisposed)
            {
                // If the time checker tab is already open, bring it to the front
                _LogoutTab = new YesNoMessageBox("คุณต้องการออกจากระบบหรือไม่?\nDo you want to log out?", "Confirm Logout", () => PerformLogout());
                _LogoutTab.FormClosed += (s, e) => _isTabOpened = false;
                _LogoutTab.Show();
                _isTabOpened = true;
            }
            else
            {
                // Open the time checker tab if it's not already open
                _LogoutTab.BringToFront();
                _LogoutTab.Focus();
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
        private static async Task<(bool IsSuccess, HttpStatusCode StatusCode, string ErrorMessage)> SendAuthRequest((string Username, string Password) credentials)
        {
            string requestUrl = "https://lib.swu.ac.th/api/auth";
            var postData = new
            {
                Username = credentials.Username,
                Password = credentials.Password,
                Action = "Cybercafe",
                ip = GetLocalIPv4Address()
            };

            try
            {
                HttpResponseMessage response = await client.PostAsJsonAsync(requestUrl, postData);

                if (response.IsSuccessStatusCode)
                {
                    return (true, response.StatusCode, null);
                }

                // Return error status for credential check handling
                string errorContent = await response.Content.ReadAsStringAsync();
                return (false, response.StatusCode, errorContent);
            }
            catch (HttpRequestException ex)
            {
                // Re-throw so BtnLogin_Click handles it in its catch block
                throw new HttpRequestException($"Network/API Error: {ex.Message}", ex, ex.StatusCode ?? HttpStatusCode.ServiceUnavailable);
            }
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

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

    }
    public class TimeCheckerForm : Form
    {
        private readonly Func<DateTime> _getTargetTime;
        private Label lblTimerDisplay;
        private System.Windows.Forms.Timer _displayTimer;
        public bool _isTabOpened;
        public TimeCheckerForm(Func<DateTime> getTargetTime)
        {
            _getTargetTime = getTargetTime;

            // Window properties
            this.Text = "Session Time Remaining";
            this.Size = new Size(400, 240);
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Right - this.Width - 10,
                Screen.PrimaryScreen.WorkingArea.Bottom - this.Height - 10
            );
            this.TopMost = true;
            // Timer Label Display
            lblTimerDisplay = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 16, FontStyle.Bold),
                ForeColor = Color.DarkBlue
            };
            this.Controls.Add(lblTimerDisplay);

            // 1-Second Refresh Timer
            _displayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _displayTimer.Tick += (s, e) => UpdateCountdownText();
            _displayTimer.Start();

            // Cleanup on form close
            this.FormClosed += (s, e) =>
            {
                //_displayTimer.Stop();
                //_displayTimer.Dispose();
                _isTabOpened = false;
            };
            
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
                _displayTimer.Stop();
            }
            else
            {
                lblTimerDisplay.Text = $"Time Remaining: {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }

    }
}