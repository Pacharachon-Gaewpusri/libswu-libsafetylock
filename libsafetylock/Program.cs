using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;


        public LockoutAuthForm()
        {
            InitializeFormComponents();
            //ApplyLockdownSettings();
        }

        private void InitializeFormComponents()
        {
            // Create controls with widths and compute horizontal center based on ClientSize
            int textBoxWidth = 150;
            int textBoxHeight = 20;
            this.txtUsername = new TextBox { Location = new System.Drawing.Point((this.ClientSize.Width - textBoxWidth) / 2, (this.ClientSize.Height - textBoxHeight) / 2), Width = textBoxWidth };
            this.txtPassword = new TextBox { Location = new System.Drawing.Point((this.ClientSize.Width - textBoxWidth) / 2, (this.ClientSize.Height / 2) + textBoxHeight), Width = textBoxWidth, PasswordChar = '*' };

            // Assume default button width ~75; you can set a specific Width if needed
            int buttonWidth = 75;
            this.btnLogin = new Button { Text = "Login", Location = new System.Drawing.Point((this.ClientSize.Width - buttonWidth) / 2, (this.ClientSize.Height - textBoxHeight) / 2 + 80), Width = buttonWidth };

            this.lblError = new Label { Location = new System.Drawing.Point((this.ClientSize.Width - 200) / 2, (this.ClientSize.Height - textBoxHeight) / 2 + 110), AutoSize = true, ForeColor = System.Drawing.Color.Red };

            this.btnLogin.Click += BtnLogin_Click;

            // Add textboxes/buttons first so we can position labels relative to them
            this.Controls.Add(this.txtUsername);
            this.Controls.Add(this.txtPassword);
            this.Controls.Add(this.btnLogin);
            this.Controls.Add(this.lblError);
            this.Controls.Add(this.lblError);
            this.Controls.Add(this.lblError);

            // Place labels to the left of textboxes
            this.Controls.Add(new Label { Text = "User:", Location = new System.Drawing.Point((this.txtUsername.Left - textBoxWidth + (this.ClientSize.Width/2)) /2 , ((this.ClientSize.Height/2) - textBoxHeight)) });
            this.Controls.Add(new Label { Text = "Password:", Location = new System.Drawing.Point((this.txtPassword.Left - textBoxWidth + (this.ClientSize.Width/2)) /2, (this.ClientSize.Height/2) ) });

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

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            if (txtUsername.Text == "admin" && txtPassword.Text == "secret123")
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

        private class Username_records
        {
            public string Username { get; set; }
            public string Password { get; set; }
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