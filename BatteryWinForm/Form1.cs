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


            Form1 a = this;
            a.StartPosition = FormStartPosition.Manual;
            a.Location = new Point(((Screen.PrimaryScreen.WorkingArea.Width - (a.Width / 2)) / 2), Screen.PrimaryScreen.WorkingArea.Height - a.Height + 20);
            a.TopMost = true;
            a.Height = 20;


            //電池電量
            Timer batteryTimer = new Timer();
            batteryTimer.Interval = 1000;// * 10;
            batteryTimer.Tick += (e, n) => {
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
                    this.Location = new Point(((Screen.PrimaryScreen.WorkingArea.Width - (a.Width/2)) / 2), Screen.PrimaryScreen.WorkingArea.Height - a.Height);
                    IsFirst = false;
                }
            };
            batteryTimer.Start();
            //this.batteryLabel.Click += (s, e) =>
            //{
            //    var batteryService = new Services.ComputerInfoService();
            //    batteryService.ShowInfo();
            //};
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
