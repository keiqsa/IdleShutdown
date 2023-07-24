using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Orvado.IdleShutdown.Common;

namespace Orvado.IdleShutdown
{
	public partial class IdleService : ServiceBase
	{
		public IdleService()
		{
			InitializeComponent();
			ProcessState = ServiceState.None;
		}

		public void Start()
		{
			LogToFile.Info("Start method called");
			myServiceStatus.currentState = (int) State.SERVICE_START_PENDING;
			SetServiceStatus(ServiceHandle, ref myServiceStatus);
			LogToFile.Info("Service status set to pending");

			if (workerThread == null ||
				threadStatus == ThreadStatus.None ||
				threadStatus == ThreadStatus.Stopped
				)
			{
				// Start a separate thread that does the actual work.
				try
				{
					LogToFile.Info("Creating worker thread");
					workerThread = new Thread(ServiceWorkerMethod);
					workerThread.Start();
					LogToFile.Info("Started worker thread");
				}
				catch (Exception ex)
				{
					LogEvent.Exception(ex, ESeverityLevel.SeverityError);
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
			SetServiceStatus(ServiceHandle, ref myServiceStatus);
		}

		private enum ServiceState
		{
			None = 0,
			IdleWait,
			ShutdownWait,
			KillUserProcess
		}
		private ServiceState ProcessState { get; set; }
        public DateTime ShutdownWaitTimeout { get; set; }
        private DateTime WaitUntil { get; set; }

		public void ServiceWorkerMethod()
		{
			try
			{
				LogToFile.Info("Service worker running"); 
				bool endThread = false;

				threadStatus = ThreadStatus.Running;
				int sleepSeconds;
				int secondsToSleep = Int32.TryParse(Resources.WorkerThreadIntervalSeconds, out sleepSeconds)
										 ? sleepSeconds
										 : 60;

				LogToFile.Info("IdleService was started successfully", ESeverityLevel.SeverityInfo);
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
						switch (ProcessState)
						{
							case ServiceState.IdleWait:
							{
								if (WaitUntil != DateTime.MinValue && DateTime.Now < WaitUntil)
								{
									break;
								}

								uint secondsIdle = IdleDetection.GetLastInputTime();
                                LogToFile.Info("Last input time " + secondsIdle);

                                if (secondsIdle > idleMinimumSeconds)
								{
									LogToFile.Info("Exceeded idle time limit of " + idleMinimumSeconds + Environment.NewLine +
									               "Calling shutdown to halt the computer");
									
									LogEvent.Info("Executing shutdown after {0} seconds of inactivity.", secondsIdle);

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

                                uint secondsIdle = IdleDetection.GetLastInputTime();
                                LogToFile.Info("Last input time " + secondsIdle);

                                if (secondsIdle < idleMinimumSeconds)
								{
                                    ProcessLauncher.ExecuteCommandLine(shutdownExe, @"-a", systemFolder);
                                    WaitUntil = DateTime.MinValue;
                                    ProcessState = ServiceState.IdleWait;

									ShutdownWaitTimeout = DateTime.MinValue;
                                }

								if (ShutdownWaitTimeout != DateTime.MinValue && DateTime.Now > ShutdownWaitTimeout)
								{
                                    LogToFile.Info("Killing all processes matching the logged on user (except shutdown itself)");
                                    ProcessLauncher.KillProcessesMatchingLoggedOnUser();
                                    
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
                                LogToFile.Info("Killing user processes did not seem to work, will retry shutdown in 2 minutes");
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
				LogEvent.Error("Exception while running main service worker thread:\r\n\r\n" + ex);
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
}
