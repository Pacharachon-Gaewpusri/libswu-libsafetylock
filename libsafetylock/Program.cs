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



namespace SecureAuthApp
{

    // ==========================================
    // 1. APPLICATION ENTRY POINT
    // ==========================================
    static class Program
    {
     

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Starts the application with the lockout form
            Application.Run(new LockoutAuthForm());

            //ProcessStartInfo psi = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "taskmgr.exe"));
            //psi.RedirectStandardOutput = false;
            //psi.WindowStyle = ProcessWindowStyle.Hidden;
            //psi.UseShellExecute = true;

            //processTaskmgr = Process.Start(psi);
        }
    }

    // ==========================================
    // 2. LOCKOUT FORM IMPLEMENTATION
    // ==========================================
    public class LockoutAuthForm : Form
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private Button btnLogin;
        private Label lblError;

        // Panel used to hold the controls so we can uniformly scale and center them
        // Store original bounds (design-time) for each child control so we can scale from them
        private readonly Dictionary<Control, Rectangle> _originalBounds = new();

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;


        private (string Username, string Password) postData = ("admin", "why123456"); // adjust values



        public LockoutAuthForm()
        {
            // Ensure DPI scaling behavior
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.StartPosition = FormStartPosition.CenterScreen;

            InitializeFormComponents();
            ApplyLockdownSettings();
        }

        private void InitializeFormComponents()
        {
            int currentWidth = this.ClientSize.Width;
            int currentHeight = this.ClientSize.Height;

            // Create controls with widths and compute horizontal center based on ClientSize
            int textBoxWidth = 160;
            int textBoxHeight = 20;
            this.txtUsername = new TextBox { Location = new System.Drawing.Point((currentWidth/2), (currentHeight/2) ), Width = textBoxWidth, Height= textBoxHeight, AutoSize = true };
            this.txtPassword = new TextBox { Location = new System.Drawing.Point((currentWidth/2), (currentHeight/2) + 25), Width = textBoxWidth, Height = textBoxHeight, PasswordChar = '*', AutoSize = true };

            // Assume default button width ~75; you can set a specific Width if needed
            int buttonWidth = 75;
            this.btnLogin = new Button { Text = "Login", Location = new System.Drawing.Point((currentWidth - buttonWidth) / 2, (currentHeight - textBoxHeight) / 2 + 80), Width = buttonWidth, AutoSize = true };
            this.AcceptButton = this.btnLogin; // Pressing Enter will trigger the login button
            this.btnLogin.Click += BtnLogin_Click;
            // Triggers login specifically when Enter is pressed inside the password textbox
            this.txtPassword.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true; // Prevents the Windows error "ding" sound
                    this.btnLogin.PerformClick();
                }
            };
            this.lblError = new Label { Location = new System.Drawing.Point((currentWidth - 200) / 2, (currentHeight - textBoxHeight) / 2 + 110), AutoSize = true, ForeColor = System.Drawing.Color.Red };
            // Add textboxes/buttons first so we can position labels relative to them
            this.Controls.Add(this.txtUsername);
            this.Controls.Add(this.txtPassword);
            this.Controls.Add(this.btnLogin);
            this.Controls.Add(this.lblError);
            this.Controls.Add(this.lblError);
            this.Controls.Add(this.lblError);

            // Place labels to the left of textboxes
            this.Controls.Add(new Label { Text = "User:", Location = new System.Drawing.Point((currentWidth - this.txtUsername.Width) / 2, (this.txtUsername.Top) ) });
            this.Controls.Add(new Label { Text = "Password:", Location = new System.Drawing.Point((currentWidth - this.txtPassword.Width) / 2, (this.txtPassword.Top) )});

            this.Load += LockoutAuthForm_Load;
            this.FormClosed += LockoutAuthForm_FormClosed;
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
            UnhookWindowsHookEx(_hookID);
        }

        private static readonly HttpClient client = new HttpClient();

        static async Task<bool> SendAuthRequest((string Username, string Password) credentials)
        {
            string requestUrl = "https://lib.swu.ac.th/api/auth";
            var postData = new
            {
                Username = credentials.Username,
                Password = credentials.Password,
                Action = "Authenticate"
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
                UnhookWindowsHookEx(_hookID);
                MessageBox.Show("Authenticated. System unlocked.");
                Application.Exit();
            }
            else
            {
                lblError.Text = "Invalid credentials.";
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
    }
}