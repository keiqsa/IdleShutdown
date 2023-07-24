using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Orvado.IdleShutdown.Common;

namespace Orvado.IdleShutdown
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main()
		{
			if (Environment.UserInteractive)
			{
                DisableConsoleQuickEdit.Go();

                // This used to run the service as a console (development phase only)
                var idleService = new IdleService();
				idleService.Start();

				Console.WriteLine(@"Press Enter to terminate ...");
				Console.ReadKey();
			}
			else
			{
				ServiceBase[] servicesToRun =
				{
					new IdleService()
				};
				ServiceBase.Run(servicesToRun);
			}
		}

        static class DisableConsoleQuickEdit
        {
            const uint ENABLE_QUICK_EDIT = 0x0040;

            // STD_INPUT_HANDLE (DWORD): -10 is the standard input device.
            const int STD_INPUT_HANDLE = -10;

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern IntPtr GetStdHandle(int nStdHandle);

            [DllImport("kernel32.dll")]
            static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

            [DllImport("kernel32.dll")]
            static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

            internal static bool Go()
            {

                IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);

                // get current console mode
                uint consoleMode;
                if (!GetConsoleMode(consoleHandle, out consoleMode))
                {
                    // ERROR: Unable to get console mode.
                    return false;
                }

                // Clear the quick edit bit in the mode flags
                consoleMode &= ~ENABLE_QUICK_EDIT;

                // set the new mode
                if (!SetConsoleMode(consoleHandle, consoleMode))
                {
                    // ERROR: Unable to set console mode
                    return false;
                }

                return true;
            }
        }
    }
}
