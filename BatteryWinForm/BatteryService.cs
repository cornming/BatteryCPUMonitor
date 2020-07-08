using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BatteryWinForm
{
    
    public static class BatteryService
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

        public static void ShowInfo()
        {
            _SYSTEM_POWER_STATUS SystemPower = new _SYSTEM_POWER_STATUS();

            GetSystemPowerStatus(ref SystemPower);

            MessageBox.Show("電力供電狀態:" + Convert.ToString(SystemPower.ACLineStatus) + "\n"
                            + "估計剩餘時間:" + Convert.ToString(SystemPower.BatteryLifeTime) + " seconds. \n"
                            + "估計電力剩餘:" + Convert.ToString(SystemPower.BatteryLifePercent) + "% \n");
        }

    }
}
