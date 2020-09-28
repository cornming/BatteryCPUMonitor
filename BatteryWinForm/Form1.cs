using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace BatteryWinForm
{
    public partial class Form1 : Form
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [DllImportAttribute("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd,
                         int Msg, int wParam, int lParam);
        [DllImportAttribute("user32.dll")]
        public static extern bool ReleaseCapture();
        bool IsFirst = true;
        public Form1()
        {
            InitializeComponent();

            //BatteryService.ShowInfo();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.MouseDown += Form1_MouseDown;
            this.batteryLabel.MouseDown += Form1_MouseDown;
            this.batteryLabel.MouseEnter += (s1, e1) =>
            {
                float size = (this.batteryLabel.Font.Size);
                this.batteryLabel.Font = new Font(new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif), size + 20, FontStyle.Bold);

                //this.AutoSize = true;


                this.Height = this.batteryLabel.Height;
                this.Width = this.batteryLabel.Width;

                this.Location = GetLocation();
            };
            this.batteryLabel.MouseLeave += (s1, e1) =>
            {
                float size = (this.batteryLabel.Font.Size);
                this.batteryLabel.Font = new Font(new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif), size - 20, FontStyle.Bold);
                //this.Location = new Point(((Screen.PrimaryScreen.WorkingArea.Width - (this.Width / 2)) / 2)-20, Screen.PrimaryScreen.WorkingArea.Height - this.Height - 20 -20);

                this.Height = this.batteryLabel.Height;
                this.Width = this.batteryLabel.Width;

                this.Location = GetLocation();
            };

            Form1 a = this;
            a.StartPosition = FormStartPosition.Manual;
            a.TopMost = true;
            a.Location = GetLocation(20);
            a.Height = 20;


            //電池電量
            Timer batteryTimer = new Timer();
            batteryTimer.Interval = 1000 * 10;
            batteryTimer.Tick += BatteryTimer_Tick;
            batteryTimer.Start();
            BatteryTimer_Tick(null, null);
            //this.batteryLabel.Click += (s, e) =>
            //{
            //    var batteryService = new Services.ComputerInfoService();
            //    batteryService.ShowInfo();
            //};
        }

        private void BatteryTimer_Tick(object sender, EventArgs e)
        {
            //更新電量狀態
            var computerInfoService = new Services.ComputerInfoService();
            float cpu = computerInfoService.GetCpuUsage();
            float ram = computerInfoService.GetRamUsage();


            //DateTime.Now.ToString("HH:mm:ss");
            int cpuRange = 255 * Convert.ToInt32(Math.Round(cpu)) / 100;

            this.batteryLabel.Text = computerInfoService.ShowPercent().Trim() +
           "CPU:" + cpu.ToString().Trim() + "% / " +
           "RAM:" + ram.ToString().Trim() + "";

            this.batteryLabel.ForeColor = Color.FromArgb(cpuRange, 255 - cpuRange, 0);

            if (IsFirst)
            {
                this.Location = GetLocation();
                IsFirst = false;
            }
        }

        private Point GetLocation(int range = 0)
        {
            return new Point(((Screen.PrimaryScreen.WorkingArea.Width - (this.Width)) / 2), Screen.PrimaryScreen.WorkingArea.Height - this.Height  + range);
        }

        private void Form1_MouseDown(object sender,
        System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void closeMenuItemClick(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
