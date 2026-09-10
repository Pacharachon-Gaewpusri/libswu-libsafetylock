using System;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using System.Timers;



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
    // 2. LOCKOUT FORM IMPLEMENTATION
    // ==========================================
    public class LockoutAuthForm : Form
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private Label UsernameLabel;
        private Label PasswordLabel;
        private Button btnLogin;
        private Label lblError;
        private System.Windows.Forms.Timer _relockTimer;

        // Store original bounds (design-time) for each child control so we can scale from them
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

            ApplyLockdownSettings();
            InitializeFormComponents();
            InitializeRelockTimer();

            // Prevent user from closing window during active lockdown
            this.FormClosing += (s, e) => {
                if (e.CloseReason == CloseReason.UserClosing)
                    e.Cancel = true;
            };
            //Debug.WriteLine($"Form size: {ClientSize.Width}x{ClientSize.Height}");

        }

        private void InitializeFormComponents()
        {

            this.txtUsername = new TextBox { AutoSize = true };
            this.txtPassword = new TextBox { PasswordChar = '*', AutoSize = true };

            // Place labels to the left of textboxes
            UsernameLabel = new Label { Text = "User:", AutoSize = true };
            PasswordLabel = new Label { Text = "Password:", AutoSize = true };

            // Assume default button width ~75; you can set a specific Width if needed

            btnLogin = new Button { Text = "Login", AutoSize = true };
            AcceptButton = this.btnLogin; // Pressing Enter will trigger the login button
            btnLogin.Click += BtnLogin_Click;

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
            
            // Add textboxes/buttons first so we can position labels relative to them
            this.Controls.Add(txtUsername);
            this.Controls.Add(txtPassword);
            this.Controls.Add(UsernameLabel);
            this.Controls.Add(PasswordLabel);
            this.Controls.Add(btnLogin);
            this.Controls.Add(lblError);

            // Hook up resize and load events to keep panel centered
            this.Load += LockoutAuthForm_Load;
            this.Resize += LockoutAuthForm_Resize;
            this.FormClosed += LockoutAuthForm_FormClosed;

            // Apply initial layout positioning
            UpdateLayout();

        }

        private void UpdateLayout()
        {
            // Query live window dimensions on demand
            int tabWidth = ClientSize.Width;
            int tabHeight = ClientSize.Height;

            int textBoxWidth = 160;
            int textboxHeight = 20;

            if (txtUsername == null || txtPassword == null) return;

            // Set dimensions and center textboxes relative to current window height & width
            txtUsername.Width = textBoxWidth;
            txtUsername.Height = textboxHeight;
            txtUsername.Location = new Point((tabWidth - textBoxWidth) / 2, (tabHeight / 2) - 40);

            txtPassword.Width = textBoxWidth;
            txtPassword.Height = textboxHeight;
            txtPassword.Location = new Point((tabWidth - textBoxWidth) / 2, (tabHeight / 2) );

            // Align labels directly to the left of textboxes
            UsernameLabel.Location = new Point(txtUsername.Left - PasswordLabel.PreferredWidth, txtUsername.Top);
            PasswordLabel.Location = new Point(txtPassword.Left - PasswordLabel.PreferredWidth , txtPassword.Top);

            // Center button and error message vertically under inputs
            btnLogin.Width = 75;
            btnLogin.Location = new Point((tabWidth - btnLogin.Width) / 2, txtPassword.Bottom + 15);

            lblError.Location = new Point((tabWidth - lblError.PreferredWidth) / 2, btnLogin.Bottom + 10);
        }
        private void LockoutAuthForm_Resize(object sender, EventArgs e)
        {
            // Recalculate control placement whenever window size or state changes
            UpdateLayout();
            // Logs the current window state along with client dimensions (usable area)
            System.Diagnostics.Debug.WriteLine(
                $"[Window Resized] State: {this.WindowState} | Width: {this.ClientSize.Width}px, Height: {this.ClientSize.Height}px"
            );
        }

        private void InitializeRelockTimer()
        {
            _relockTimer = new System.Windows.Forms.Timer();
            _relockTimer.Interval = 600000; // 10 minutes in milliseconds
            _relockTimer.Tick += RelockTimer_Tick;
        }
        private void ApplyLockdownSettings()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.ShowInTaskbar = false;

            this.FormClosing += (s, e) => {
                if (e.CloseReason == CloseReason.UserClosing)
                    e.Cancel = true;
            };
        }
        private void LockoutAuthForm_Load(object sender, EventArgs e)
        {
            _hookID = SetHook(_proc);

        }

        private void LockoutAuthForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _relockTimer?.Stop();
            _relockTimer?.Dispose();

            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private static readonly HttpClient client = new HttpClient();

        static async Task<bool> SendAuthRequest((string Username, string Password) credentials)
        {
            string requestUrl = "https://lib.swu.ac.th/api/auth";
            var postData = new
            {
                Username = credentials.Username,
                Password = credentials.Password,
                Action = "Cybercafe",
                ip = ""
            };

            HttpResponseMessage response = await client.PostAsJsonAsync(requestUrl, postData);
            return response.IsSuccessStatusCode;
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
                this.Hide();

                // Begin 10-minute countdown
                _relockTimer.Start();
                //After 1 minute, system sends a warning message.
                MessageBox.Show("เวลาใกล้หมด กรุณาบันทึกงานของคุณ\n Time is almost up. Please save your work.");
            }
            else
            {
                lblError.Text = "Invalid credentials.";
                UpdateLayout();
            }

           
        }
        private void RelockTimer_Tick(object sender, EventArgs e)
        {
            _relockTimer.Stop();

            // Re-enable low-level keyboard interception
            if (_hookID == IntPtr.Zero)
            {
                _hookID = SetHook(_proc);
            }

            // Restore lockdown view and force to front
            ApplyLockdownSettings();
            this.Show();
            this.WindowState = FormWindowState.Maximized;
            this.BringToFront();
            this.Activate();
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