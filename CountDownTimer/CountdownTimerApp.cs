namespace CountDownTimer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            private void InitializeCountdownTimer()
            {
                // Initialize 1-second countdown update timer
                _countdownTimer = new System.Windows.Forms.Timer();
                _countdownTimer.Interval = 1000; // 1000 ms = 1 second
                _countdownTimer.Tick += CountdownTimer_Tick;
            }
            private void RemainingTimeTabLayout()
            {
                int TimeCheckertabWidth = 400;
                int TimeCheckertabHeight = 240;

                CountdownTimer();
                //// Hide authentication controls
                //txtUsername.Visible = false;
                //txtPassword.Visible = false;
                //UsernameLabel.Visible = false;
                //PasswordLabel.Visible = false;
                //btnLogin.Visible = false;
                //lblError.Visible = false;
                //// Show standby controls
                //TrayNotiIcon.Visible = true;
                //// Make unclickable countdown button visible and position it
                //CheckTimeRemainingbtn.Visible = true;
                //CheckTimeRemainingbtn.Location = new Point((TimeCheckertabWidth - CheckTimeRemainingbtn.Width) / 2, (TimeCheckertabHeight- CheckTimeRemainingbtn.Height)/2);

                //ConfirmLogoutBtn.Visible = false;
            }
        }
    }
}
