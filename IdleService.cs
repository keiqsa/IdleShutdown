using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpDX.XInput;

namespace Keiqsa_IdleShutdown.Core;
public class IdleService
{
    private Controller controller1;
    private Controller controller2;
    private Controller controller3;
    private Controller controller4;

    private SharpDX.XInput.State previousState1;
    private SharpDX.XInput.State previousState2;
    private SharpDX.XInput.State previousState3;
    private SharpDX.XInput.State previousState4;

    private DateTime lastInputTime = DateTime.Now;

    public IdleService()
    {
        controller1 = new Controller(UserIndex.One);
        controller2 = new Controller(UserIndex.Two);
        controller3 = new Controller(UserIndex.Three);
        controller4 = new Controller(UserIndex.Four);
    }

    public void Start()
    {
        LogEvent.Info("Start method called");
        myServiceStatus.currentState = (int)State.SERVICE_START_PENDING;

        LogEvent.Info("Service status set to pending");

        if (workerThread == null ||
            threadStatus == ThreadStatus.None ||
            threadStatus == ThreadStatus.Stopped
            )
        {
            // Start a separate thread that does the actual work.
            try
            {
                LogEvent.Info("Creating worker thread");
                workerThread = new Thread(ServiceWorkerMethod);
                workerThread.Start();
                LogEvent.Info("Started worker thread");
            }
            catch (Exception ex)
            {
                LogEvent.Info(ex.ToString());
            }
        }
        else
        {
            LogEvent.Info("Unable to start thread, due to following:" + Environment.NewLine +
                "\tWorker thread NULL: " + (workerThread == null ? "True" : "False") + Environment.NewLine +
                "\tThread status: " + threadStatus);
        }
        if (workerThread != null)
        {
            LogEvent.Info("Start - Worker thread state: {0}\r\nThread status: {1}",
                workerThread.ThreadState.ToString(), threadStatus.ToString());
        }

        myServiceStatus.currentState = (int)State.SERVICE_RUNNING;
    }

    private enum ServiceState
    {
        None = 0,
        IdleWait,
        ShutdownWait,
        KillUserProcess
    }
    private ServiceState ProcessState
    {
        get; set;
    }
    public DateTime ShutdownWaitTimeout
    {
        get; set;
    }
    private DateTime WaitUntil
    {
        get; set;
    }

    public void ServiceWorkerMethod()
    {
        try
        {
            LogEvent.Info("Service worker running");
            bool endThread = false;

            threadStatus = ThreadStatus.Running;
            int sleepSeconds;
            int secondsToSleep = 5;

            LogEvent.Info("IdleService was started successfully");
            ProcessState = ServiceState.IdleWait;
            WaitUntil = DateTime.MinValue;
            int idleMinimumSeconds = 35 * 60;

            string systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string shutdownExe = Path.Combine(systemFolder, "shutdown.exe");

            while (!endThread)
            {
                if (pauseEvent.WaitOne(0))
                {
                    threadStatus = ThreadStatus.Paused;
                    LogEvent.Info("Pause signal received at " + DateTime.Now);
                }
                else if (threadStatus != ThreadStatus.Paused)
                {
                    try
                    {
                        var currentState1 = controller1.GetState();
                        if (!currentState1.Equals(previousState1))
                        {
                            previousState1 = currentState1;
                            lastInputTime = DateTime.Now;
                        }
                    }
                    catch (Exception ex)
                    {
                    }

                    try
                    {
                        var currentState2 = controller2.GetState();
                        if (!currentState2.Equals(previousState2))
                        {
                            previousState2 = currentState2;
                            lastInputTime = DateTime.Now;
                        }
                    }
                    catch (Exception ex)
                    {
                    }

                    try
                    {
                        var currentState3 = controller3.GetState();
                        if (!currentState3.Equals(previousState3))
                        {
                            previousState3 = currentState3;
                            lastInputTime = DateTime.Now;
                        }
                    }
                    catch (Exception ex)
                    {
                    }

                    try
                    {
                        var currentState4 = controller4.GetState();
                        if (!currentState4.Equals(previousState4))
                        {
                            previousState4 = currentState4;
                            lastInputTime = DateTime.Now;
                        }

                    }
                    catch (Exception ex)
                    {
                    }

                    var secondsIdle = Math.Round((DateTime.Now - lastInputTime).TotalSeconds);

                    switch (ProcessState)
                    {
                        case ServiceState.IdleWait:
                        {
                            if (WaitUntil != DateTime.MinValue && DateTime.Now < WaitUntil)
                            {
                                break;
                            }

                            LogEvent.Info("Last input time " + secondsIdle);

                            if (secondsIdle > idleMinimumSeconds)
                            {
                                LogEvent.Info("Exceeded idle time limit of " + idleMinimumSeconds + Environment.NewLine +
                                                "Calling shutdown to halt the computer");

                                LogEvent.Info("Executing shutdown after {0} seconds of inactivity.", secondsIdle.ToString());

                                ProcessLauncher.ExecuteCommandLine(shutdownExe, @"/s /t 300", systemFolder);

                                WaitUntil = DateTime.MinValue; //TODO: Convert to constant
                                ProcessState = ServiceState.ShutdownWait;

                                ShutdownWaitTimeout = DateTime.Now.AddSeconds(360);
                            }
                            break;
                        }
                        case ServiceState.ShutdownWait:
                        {
                            if (WaitUntil != DateTime.MinValue && DateTime.Now < WaitUntil)
                            {
                                break;
                            }

                            LogEvent.Info("Last input time " + secondsIdle);

                            if (secondsIdle < idleMinimumSeconds)
                            {
                                ProcessLauncher.ExecuteCommandLine(shutdownExe, @"-a", systemFolder);

                                WaitUntil = DateTime.MinValue;
                                ProcessState = ServiceState.IdleWait;

                                ShutdownWaitTimeout = DateTime.MinValue;
                            }

                            if (ShutdownWaitTimeout != DateTime.MinValue && DateTime.Now > ShutdownWaitTimeout)
                            {
                                LogEvent.Info("Killing all processes matching the logged on user (except shutdown itself)");
                                // ProcessLauncher.KillProcessesMatchingLoggedOnUser();

                                WaitUntil = DateTime.Now.AddSeconds(60); //TODO: Convert to constant
                                ProcessState = ServiceState.KillUserProcess;
                            }
                            break;
                        }
                        case ServiceState.KillUserProcess:
                        {
                            if (WaitUntil != DateTime.MinValue && DateTime.Now < WaitUntil)
                            {
                                break;
                            }

                            ProcessLauncher.ExecuteCommandLine(shutdownExe, @"/s", systemFolder);

                            // Nothing left to do but start the process over
                            LogEvent.Info("Killing user processes did not seem to work, will retry shutdown in 2 minutes");
                            WaitUntil = DateTime.Now.AddMinutes(2);
                            ProcessState = ServiceState.IdleWait;
                            break;
                        }
                    }
                }
                else if (continueThread.WaitOne(0))
                {
                    threadStatus = ThreadStatus.Running;
                }

                for (int i = 0; i < secondsToSleep && !endThread; i++)
                {
                    Thread.Sleep(1000);
                    if (stopEvent.WaitOne(0))
                    {
                        endThread = true;
                        LogEvent.Info("Stop event signaled at " + DateTime.Now.ToString());
                    }
                }
            }
        }
        catch (ThreadAbortException)
        {
            LogEvent.Info("Worker thread has been aborted, shutting down");
        }
        catch (Exception ex)
        {
            LogEvent.Info("Exception while running main service worker thread:\r\n\r\n" + ex);
        }
        finally
        {
            threadStatus = ThreadStatus.Stopped;
            LogEvent.Info("Main service worker thread exiting");
        }
    }

    public enum ThreadStatus
    {
        None = 0,
        Running,
        Paused,
        Stopped,
    }

    private Thread workerThread;
    private static readonly ManualResetEvent pauseEvent = new ManualResetEvent(false);
    private static readonly ManualResetEvent continueThread = new ManualResetEvent(false);
    private static readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
    private static volatile ThreadStatus threadStatus;

    private SERVICE_STATUS myServiceStatus;

    #region Service Helpers

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public int serviceType;
        public int currentState;
        public int controlsAccepted;
        public int win32ExitCode;
        public int serviceSpecificExitCode;
        public int checkPoint;
        public int waitHint;
    }

    public enum State
    {
        SERVICE_STOPPED = 0x00000001,
        SERVICE_START_PENDING = 0x00000002,
        SERVICE_STOP_PENDING = 0x00000003,
        SERVICE_RUNNING = 0x00000004,
        SERVICE_CONTINUE_PENDING = 0x00000005,
        SERVICE_PAUSE_PENDING = 0x00000006,
        SERVICE_PAUSED = 0x00000007,
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int SetServiceStatus(
        IntPtr hServiceStatus,
        ref SERVICE_STATUS lpServiceStatus);

    #endregion
}

internal class LogEvent
{
    internal static void Info(params string[] strs) => Console.WriteLine(string.Join(' ', strs));
}