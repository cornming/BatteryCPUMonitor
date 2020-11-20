using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BatteryWinForm.Services
{
    public class ComputerInfoService
    {
        [DllImport("kernel32", EntryPoint = "GetSystemPowerStatus")]
        private static extern void GetSystemPowerStatus(ref _SYSTEM_POWER_STATUS systemPowerStatus);

        public struct _SYSTEM_POWER_STATUS
        {
            public Byte ACLineStatus;                //0 = offline,  1 = Online, 255 = UnKnown Status.
            public Byte BatteryFlag;
            public Byte BatteryLifePercent;
            public Byte Reserved1;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }
        /// <summary>
        /// 秀出電源資訊
        /// </summary>
        public void ShowInfo()
        {
            _SYSTEM_POWER_STATUS SystemPower = new _SYSTEM_POWER_STATUS();

            GetSystemPowerStatus(ref SystemPower);

            MessageBox.Show("電力供電狀態:" + Convert.ToString(SystemPower.ACLineStatus) + "\n"
                            + "BatteryFlag:" + Convert.ToString(SystemPower.BatteryFlag) + "\n"
                            + "估計電力剩餘:" + Convert.ToString(SystemPower.BatteryLifePercent) + "% \n"
                            + "Reserved1:" + Convert.ToString(SystemPower.Reserved1) + "\n"
                            + "估計剩餘時間:" + Convert.ToString(SystemPower.BatteryLifeTime) + " seconds. \n"
                            + "BatteryFullLifeTime:" + Convert.ToString(SystemPower.BatteryFullLifeTime) + " seconds. \n"
                            + "");
        }
        /// <summary>
        /// 秀出電量百分比
        /// </summary>
        /// <returns></returns>
        public string ShowPercent()
        {
            _SYSTEM_POWER_STATUS SystemPower = new _SYSTEM_POWER_STATUS();

            GetSystemPowerStatus(ref SystemPower);

            string BatteryLifePercent = $@"電量:{Convert.ToString(SystemPower.BatteryLifePercent)}%{" / "}";
            var status = Convert.ToString(SystemPower.ACLineStatus);
            string ACLineStatus = "";

            switch (status)
            {
                case "0":
                    ACLineStatus = "";//未充電
                    break;
                case "1":
                    ACLineStatus = "充電中";
                    break;
                default:
                    ACLineStatus = "查無充電裝置";//查無充電裝置
                    break;
            }
            ACLineStatus += ACLineStatus == "" ? "" : " / ";

            return $@"{BatteryLifePercent}{ACLineStatus}";
        }

        #region CPU、Memory
        PerformanceCounter cpuUsage = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        PerformanceCounter ramUsage = new PerformanceCounter("Memory", "Available MBytes");
        /// <summary>
        /// 取得CPU使用量
        /// </summary>
        /// <param name="cpuUsage"></param>
        /// <returns></returns>
        public string GetCpuUsage()
        {
            float _usage = 0;

            for (int i = 0; i < 10; i++)
            {
                System.Threading.Thread.Sleep(200); //休息200ms以避免只抓到0或100.
                _usage += cpuUsage.NextValue();
            }

            return string.Format("{0:n0}",_usage / 9); //第一次抓到的值為零,所以捨去不計.
        }
        /// <summary>
        /// 取得記憶體使用量
        /// </summary>
        /// <param name="ramUsage"></param>
        /// <returns></returns>
        public string GetRamUsage()
        {
            int ramRound = int.Parse(Math.Round(ramUsage.NextValue()/100, 0).ToString());
            return string.Format("{0:n0}", ramRound);
        }
        #endregion
    }
}
