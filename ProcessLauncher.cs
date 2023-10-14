using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Keiqsa_IdleShutdown.Core;
internal class ProcessLauncher
{
    public static string Output
    {
        get; private set;
    }

    private static Process process;
    private static ProcessStartInfo processInfo;
    private static ProcessStartInfo ProcessInfo => processInfo ?? (processInfo = new ProcessStartInfo
    {
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        Verb = "runas"
    });

    public static void ExecuteCommandLine(string executableName, string argumentToExecute,
        string workingDirectory)
    {
        const int timeOut = 5 * 60 * 1000;

        LogEvent.Info("Execute: {0} {1}", executableName, argumentToExecute);
        ProcessInfo.FileName = executableName;
        ProcessInfo.Arguments = argumentToExecute;
        if (workingDirectory != null)
        {
            ProcessInfo.WorkingDirectory = workingDirectory;
        }
        process = Process.Start(ProcessInfo);
        if (process != null)
        {
            Output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(timeOut);

            if (!process.HasExited)
            {
                if (process.Responding)
                {
                    process.CloseMainWindow();
                }
                else
                {
                    process.Kill();
                    LogEvent.Info("Process timed out after 5 minutes.");
                }
            }
            if (process.ExitCode != 0)
            {
                LogEvent.Info("\r\nTotal time spend: " + process.TotalProcessorTime.TotalSeconds +
                               "\r\nExit Code: " + process.ExitCode + "\r\n" + argumentToExecute + "\r\n" +
                               "\r\nOutput:\r\n" + Output);
            }
        }
    }

    private static bool EndLocalProcesses(Process[] localProcesses, bool closeWindowFirst)
    {
        string processTitle = "NONE";
        bool success = true;
        try
        {
            if (closeWindowFirst)
            {
                foreach (Process p in localProcesses)
                {
                    processTitle = p.ProcessName + " (" + p.Id + ")";
                    if (p.Responding)
                    {
                        LogEvent.Info("Requesting to close main window for process {0} (id = {1})",
                                       p.ProcessName, p.Id.ToString());
                        p.CloseMainWindow();
                    }
                    else
                    {
                        LogEvent.Info("Process {0} (id = {1}) is not responding", p.ProcessName, p.Id.ToString());
                    }
                }
            }

            Thread.Sleep(200);
            foreach (Process p in localProcesses)
            {
                processTitle = p.ProcessName + " (" + p.Id + ")";
                p.Refresh();
                if (!p.HasExited)
                {
                    LogEvent.Info("Killing process {0} (id = {1})", p.ProcessName, p.Id.ToString());
                    p.Kill();
                }
            }
        }
        catch (NotSupportedException ex)
        {
            LogEvent.Info("Unable to kill process \"{0}\"\r\n{1}", processTitle, ex.ToString());
            success = false;
        }
        catch (InvalidOperationException ex)
        {
            LogEvent.Info("Unable to kill process \"{0}\"\r\n{1}", processTitle, ex.ToString());
            success = false;
        }
        return success;
    }

    /// <summary>
    /// Kill all processes matching the name "processName".  This name should
    /// not include the extension (like ".exe")
    /// </summary>
    /// <param name="processId">ID of the process to kill</param>
    /// <param name="closeWindowFirst">Attempt the close window first before killing the process</param>
    private static bool KillProcessById(int processId, bool closeWindowFirst)
    {
        bool success;
        try
        {
            LogEvent.Info("Killing processes matching ID \"{0}\"", processId.ToString());

            // Get all instances of process (matching processName)
            Process localProcess = Process.GetProcessById(processId);

            success = EndLocalProcesses(new[] { localProcess }, closeWindowFirst);
        }
        catch (Exception ex)
        {
            LogEvent.Info("Unable to kill processes by ID \"{0}\"\r\n{1}",
                         processId.ToString(), ex.ToString());
            success = false;
        }

        // check that the process is gone
        if (success)
        {
            try
            {
                Process checkProcess = null;
                for (int count = 0; count < 10; count++)
                {
                    Thread.Sleep(200);
                    checkProcess = Process.GetProcessById(processId);
                }

                LogEvent.Info("Error checking process was ended \"{0}\"\r\n    {1}",
                    processId.ToString(), (checkProcess != null) ? checkProcess.ToString() : "(NULL)");
                success = false;
            }
            catch (ArgumentException)
            {
                //Expected result because the ProcessID no longer exists
            }
            catch (Exception ex)
            {
                LogEvent.Info("Unable to get processes by ID \"{0}\"\r\n{1}",
                             processId.ToString(), ex.ToString());
                success = false;
            }
        }
        return success;
    }

    private static string GetProcessUserName(string procName)
    {
        string query = "SELECT * FROM Win32_Process WHERE Name = \'" + procName + ".exe\'";
        var procs = new ManagementObjectSearcher(query);
        foreach (ManagementBaseObject o in procs.Get())
        {
            var p = (ManagementObject)o;
            var path = p["ExecutablePath"];
            if (path != null)
            {
                string executablePath = path.ToString();
                object[] ownerInfo = new object[2];
                p.InvokeMethod("GetOwner", ownerInfo);
                return (string)ownerInfo[0];
            }
        }
        return null;
    }

    public static void KillProcessesMatchingLoggedOnUser()
    {
        string loggedOnUser = Environment.UserName;
        if (String.IsNullOrEmpty(loggedOnUser))
        {
            LogEvent.Info("Unable to kill user process because current logged on user is empty");
            return;
        }
        LogEvent.Info("Killing all processes owned by user \"{0}\"", loggedOnUser);

        int attemptCount = 0;
        int successCount = 0;
        string pattern = @"^.*" + Regex.Escape(loggedOnUser) + @".*$";
        Process[] runningProcesses = Process.GetProcesses();
        foreach (Process p in runningProcesses)
        {
            if (p.ProcessName.StartsWith("IdleShutdown", StringComparison.InvariantCulture))
            {
                continue;
            }
            string processOwner = GetProcessUserName(p.ProcessName) ?? "";
            if (Regex.IsMatch(processOwner, pattern, RegexOptions.IgnoreCase))
            {
                bool success = KillProcessById(p.Id, true);
                attemptCount++;
                successCount += (success ? 1 : 0);
            }
        }

        LogEvent.Info("Successfully killed {0} processes out of {1} owned by user \"{2}\"",
            successCount.ToString(), attemptCount.ToString(), loggedOnUser);
    }
}
