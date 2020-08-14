using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AzureIoTAgent
{
    class Common
    {
        static CommonLogging logging = new CommonLogging(new JObject());
        static DeviceClient deviceClient;
        static JObject config;
        static Authenticate authenticate;
        static PluginFramework pf;

        static int reportTWINIntervalMinutes = 60 * 8;          // should add a TWIN property to address this?

        static async Task Main(string[] args)
        {
            logging.log("AzureIoTAgent starting");

            try
            {
                config = JObject.Parse(File.ReadAllText("config.json"));
            }
            catch (Exception er)
            {
                logging.log("Error opening config.json " + er.ToString());
                return;
            }

            logging = new CommonLogging(config);
            authenticate = new Authenticate(logging);

            // should add auto provision functions here  considerging a single method with multiple parameters
            deviceClient = await authenticate.AuthenticateWithConnectionString(config["connectionString"].ToString());
            deviceClient.SetConnectionStatusChangesHandler(connectionChangeHandler);

            await deviceClient.SetMethodHandlerAsync("autoUpdate", autoUpdateHandler, null);
            await deviceClient.SetMethodHandlerAsync("restart", restartHandler, null);

            logging.setDeviceClient(deviceClient);

            pf = new PluginFramework(deviceClient, config, logging);
            await deviceClient.SetDesiredPropertyUpdateCallbackAsync(pf.pluginTWINcallback, null);
            // get the initial TWIN
            pf.pluginTWINcallback(null, null);

            await reportConfigFile();
            
            // start a thread for Remote Stream connections
            // here is a sample client: https://github.com/Azure-Samples/azure-iot-samples-csharp/tree/master/iot-hub/Quickstarts/device-streams-proxy/service
            Thread remoteStream = new Thread(remoteStreamThread);
            remoteStream.Start();

            Thread reportHardware = new Thread(reportHardwareThread);
            reportHardware.Start();

            Thread certificateManagement = new Thread(certificateManagementThread);
            //certificateManagement.Start();
            
            // need to evaluate if this is needed.  if starting offline (disconnected), and then reconnect
            //                                      do we get a TWIN update?  perhaps add the 'get twin' to the device reconnect logic
            //Thread pluginManagement = new Thread(pluginManagementThread);
            //pluginManagement.Start();
            
            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        private static void connectionChangeHandler(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            logging.log("ConnectionStatus: " + status.ToString() + " " + reason.ToString());
            if (status == ConnectionStatus.Disconnected  || status == ConnectionStatus.Disabled)
            {
                logging.log(String.Format("ConnectionStatus: {0} {1}", status, reason), 1);
            }
        }

        private static async void pluginManagementThread()
        {
            await pf.ProcessPluginsForever(new CancellationToken());
        }

        private static async void certificateManagementThread()
        {
            CertificateManagement cs = new CertificateManagement(deviceClient, config, logging);
            await cs.ProcessCertificatestForever(new CancellationToken());
        }

        private static async void remoteStreamThread()
        {
            RemoteStream rs = new RemoteStream(deviceClient, config, logging);
            await rs.DeviceStreamListenForever(new CancellationTokenSource());
        }

        private static async void reportHardwareThread()
        {
            while (true)
            {
                await reportHardware();
                Thread.Sleep(1000 * 60 * reportTWINIntervalMinutes);
            }
        }

        private static Task<MethodResponse> restartHandler(MethodRequest methodRequest, object userContext)
        {
            logging.log("restartHandler()");

            ProcessStartInfo info = new ProcessStartInfo();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                info.FileName = "shutdown";
                info.Arguments = "-r +1";
            } else
            {
                info.FileName = "shutdown";
                info.Arguments = "/r /t 60 /c IoTAgent /f /d p:4:1";
            }

            info.UseShellExecute = true;
            info.CreateNoWindow = true;
            Process.Start(info).WaitForExit();

            MethodResponse resp = new MethodResponse(Encoding.UTF8.GetBytes("{\"Status\": \"OK\"}"), 200);
            return Task.FromResult(resp);
        }

        private static Task<MethodResponse> autoUpdateHandler(MethodRequest methodRequest, object userContext)
        {
            logging.log("autoUpdateHandler()");
            MethodResponse resp = new MethodResponse(Encoding.UTF8.GetBytes("{\"Status\": \"OK\"}"), 200);
            return Task.FromResult(resp);
        }

        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        static Task reportHardware()
        {
            logging.log("reportHardware()");

            JObject hardwareTWIN = new JObject();

            hardwareTWIN["reportTime"] = DateTime.Now;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try{hardwareTWIN["HostName"] = Environment.MachineName; } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                try{hardwareTWIN["OS"] = runCommand("/bin/sh", "cat /etc/issue"); } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                try{hardwareTWIN["CPU"] = runCommand("/bin/sh", "cat /proc/cpuinfo | grep 'model name' | head -n 1 | cut -d ':' -f2"); } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                try {hardwareTWIN["CPUCount"] = Convert.ToInt32(runCommand("/bin/sh", "cat /proc/cpuinfo | grep 'processor' | tail -n 1 | cut -d ':' -f2")) + 1; } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                // this requires apt install lm-sensors
                try {hardwareTWIN["CPUTemp"] = runCommand("/bin/sh", "sensors | grep 'Package' | cut -d' ' -f5"); } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                try {hardwareTWIN["RAMTotal"] = runCommand("/bin/sh", "free | tr -s ' ' | sed '/^Mem/!d' | cut -d' ' -f2"); } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                try {hardwareTWIN["RAMFree"] = runCommand("/bin/sh", "free | tr -s ' ' | sed '/^Mem/!d' | cut -d' ' -f4"); } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                try {hardwareTWIN["DiskTotal"] = runCommand("/bin/sh", "df -l -a -t ext4 | grep '/' | head -n 1 | tr -s ' ' | cut -d' ' -f1,2"); } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                try {hardwareTWIN["DiskFree"] = runCommand("/bin/sh", "df -l -a -t ext4 | grep '/' | head -n 1 | tr -s ' ' | cut -d' ' -f1,4"); } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
                try {hardwareTWIN["DockerVersion"] = runCommand("/bin/sh", "docker -v"); } catch (Exception er) { logging.log("reportHardware() er=" + er.ToString(), 1); }
            } else
            {
                try { hardwareTWIN["HostName"] = Environment.MachineName; } catch (Exception) { }
                try { hardwareTWIN["OS"] = Environment.OSVersion.VersionString; } catch (Exception) { }
                try { hardwareTWIN["CPU"] = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER").Trim(); } catch (Exception) { }
                try { hardwareTWIN["CPUCount"] = Convert.ToInt16(Environment.GetEnvironmentVariable("NUMBER_OF_PROCESSORS")); } catch (Exception) { }

                try
                {
                    string output;
                    
                    ProcessStartInfo info = new ProcessStartInfo();
                    info.FileName = "wmic";
                    info.Arguments = "OS get FreePhysicalMemory,TotalVisibleMemorySize /Value";
                    info.RedirectStandardOutput = true;

                    using (var process = Process.Start(info))
                    {
                        output = process.StandardOutput.ReadToEnd();
                    }

                    var lines = output.Trim().Split("\n");
                    hardwareTWIN["RAMFree"] = Convert.ToInt64(lines[0].Split("=", StringSplitOptions.RemoveEmptyEntries)[1]);
                    hardwareTWIN["RAMTotal"] = Convert.ToInt64(lines[1].Split("=", StringSplitOptions.RemoveEmptyEntries)[1]);
                }
                catch (Exception){}

                try
                {
                    DriveInfo driveInfo = new DriveInfo(Directory.GetDirectoryRoot("C:\\"));
                    hardwareTWIN["DiskTotal"] = driveInfo.TotalSize;
                    hardwareTWIN["DiskFree"] = driveInfo.AvailableFreeSpace;
                }
                catch (Exception){}

                try
                {
                    string output;

                    ProcessStartInfo info = new ProcessStartInfo();
                    info.FileName = "docker";
                    info.Arguments = "-v";
                    info.RedirectStandardOutput = true;

                    using (var process = Process.Start(info))
                    {
                        output = process.StandardOutput.ReadToEnd();
                    }

                    var lines = output.Trim().Split("\n");
                    hardwareTWIN["DockerVersion"] = lines[0];
                }
                catch (Exception) { }

            }

            TwinCollection twinCollection = new TwinCollection(hardwareTWIN.ToString());
            deviceClient.UpdateReportedPropertiesAsync(twinCollection).Wait();

            return Task.CompletedTask;
        }

        static string runCommand(string command, string parameters)
        {
            string output;

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = command;
            if (command == "/bin/sh")
            {
                parameters = "-c \"" + parameters + "\"";
            }
            info.Arguments = parameters;
            info.RedirectStandardOutput = true;
            info.CreateNoWindow = true;

            using (var process = Process.Start(info))
            {
                output = process.StandardOutput.ReadToEnd();
            }

            return output.Replace("\n", "").Replace("\r", "");
        }

        static Task reportConfigFile()
        {
            // report the configFile as a TWIN
            JObject reportConfig = new JObject
            {
                ["configFile"] = config
            };
            deviceClient.UpdateReportedPropertiesAsync(new TwinCollection(reportConfig.ToString())).Wait();

            return Task.CompletedTask;
        }
    }
}
