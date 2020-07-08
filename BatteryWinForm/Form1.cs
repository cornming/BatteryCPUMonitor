using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BatteryWinForm
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            //BatteryService.ShowInfo();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Form1 a = this;
            a.StartPosition = FormStartPosition.Manual;
            a.Location = new Point(0,Screen.PrimaryScreen.WorkingArea.Height - a.Height);
            a.TopMost = true;


            //電池電量
            Timer batteryTimer = new Timer();
            batteryTimer.Interval = 1000 * 10;
            batteryTimer.Tick += (e, n) => {
                //更新電量狀態
                var computerInfoService = new Services.ComputerInfoService();
                float cpu = computerInfoService.GetCpuUsage();
                float ram = computerInfoService.GetRamUsage();

                this.batteryLabel.Text = computerInfoService.ShowPercent() +
               "CPU:" + cpu.ToString() + "% / " +
               "RAM:" + ram.ToString() + "";
                //DateTime.Now.ToString("HH:mm:ss");
                int cpuRange = 255 * Convert.ToInt32(Math.Round(cpu)) / 100;
                this.batteryLabel.ForeColor = Color.FromArgb(cpuRange, 255 - cpuRange, 0);
            };
            batteryTimer.Start();
            //this.batteryLabel.Click += (s, e) =>
            //{
            //    var batteryService = new Services.ComputerInfoService();
            //    batteryService.ShowInfo();
            //};
        }


        private void closeMenuItemClick(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
