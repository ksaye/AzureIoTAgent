using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq.Expressions;

namespace AzureIoTAgent
{
    /*  Here is the schema (TWIN) which drives the plugin
      "PluginFramework": [
        {
          "name": "Monitor Memory Usage",
          "id": "e408d6db-23fa-4bae-b3bb-2be9c0371834",
          "type": "External",
          "runAtStartup": true,                                                                  // to run at startup and always run (restart if neeed)
          "startTimeEPOC": "1592317357",                                                         // to run in the future
          "command": "c:\\program~1\\python.exe",
          "parameters": "-u c:\\scripts\\monitorMemory.py",
          "stdOutToMessage": "c:\\scripts\\monitorMemory.stdOutToMessage",                      
          "stdOutToTWIN": "c:\\scripts\\monitorMemory.stdOutTWIN",                              
          "fileSha1Hash": "da39a3ee5e6b4b0d3255bfef95601890afd80709",
          "stdInTWIN": "c:\\scripts\\monitorMemory.stdInTWIN",                                  // not working, how do we get the TWIN to python.exe?  
          "nonZeroExitPlugin": "44a021a4-895b-44e2-8406-5ea53f3a35e7",                          
          "payloadDependency": "75f24c6f-bb08-49c4-ba83-8d15809a12a3"
        },
        {
          "name": "Monitor Memory Usage Payload",
          "id": "75f24c6f-bb08-49c4-ba83-8d15809a12a3",
          "type": "Payload",
          "startTimeEPOC": "1592317357",
          "downloadBandwidthRate": 50,                                                          // not implemented yet
          "downloadFileURL": "https://aaa.com/mypayload.zip?sastoken=abc123",
          "fileSha1Hash": "da39a3ee5e6b4b0d3255bfef95601890afd80709",
          "downloadFile": "c:\\scripts\\mypayload.zip",
          "postDownloadCommand": "unzip.exe c:\\scripts\\mypayload.zip c:\\scripts"
          "payloadDependency": "75f24c6f-bb08-49c4-ba83-8d15809a12a3"                           // note Payloads we can have nested dependencies
        },
        {
          "name": "Monitor Processor Usage",                                                    // not tested yet
          "id": "568badd2-5f81-43f3-89fa-26897dcaffbc",
          "type": "Code",
          "runAtStartup": true,
          "codeFileName": "c:\\scripts\\mycode.dll",
          "fileSha1Hash": "da39a3ee5e6b4b0d3255bfef95601890afd80709",
          "nonZeroExitPlugin": "97b52324-6752-4490-ad19-df36cc0a21d4",
          "payloadDependency": "60b10ad3-786a-4ce7-a94a-10757181e670"
        }
      ],
     */
    class PluginFramework
    {
        static CommonLogging _logging;
        static DeviceClient _deviceClient;
        static List<Guid> _runningPlugins = new List<Guid>();
        static JObject _config;
        static JArray _twin;
        static SHA1Managed sha1 = new SHA1Managed();

        const int info = 0;
        const int error = 1;

        public PluginFramework(DeviceClient deviceClient, JObject config, CommonLogging logging)
        {
            _logging = logging;
            _logging.log("Starting PluginFramework");
            _deviceClient = deviceClient;
            _config = config;

            // for some reason, this is not working
            //_deviceClient.SetDesiredPropertyUpdateCallbackAsync(pluginTWINcallback, null).Wait();
        }

        public async Task ProcessPluginsForever(CancellationToken cancellationToken)
        {
            _logging.log("ProcessPluginsForever");
            while (!cancellationToken.IsCancellationRequested)
            {
                //await _deviceClient.SendEventAsync(new Message(Encoding.UTF8.GetBytes("a")));
                await pluginTWINcallback((await _deviceClient.GetTwinAsync()).Properties.Desired, null);
                Thread.Sleep(1000 * 60);
            }
        }

        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        public async Task<Task> pluginTWINcallback(TwinCollection desiredProperties, object userContext)
        {
            //_logging.log("pluginTWINcallback start");
            // because we need the entire TWIN, can not use desiredProperties because it could be a PATCH
            JObject twinJobject = JObject.Parse((await _deviceClient.GetTwinAsync()).Properties.Desired.ToJson());
//            _logging.log("TWIN Received: " + twinJobject.ToString(formatting: Formatting.None));

            if (!twinJobject.ContainsKey("PluginFramework"))
            {
                return Task.CompletedTask;
            }

            _twin = twinJobject["PluginFramework"].ToObject<JArray>();
//            _logging.log("running processes are: " + string.Join(",", _runningPlugins.ToArray()));

            foreach (JObject pluginObject in _twin)
            {
                plugin _plugin = JsonConvert.DeserializeObject<plugin>(pluginObject.ToString());
                switch (_plugin.type)
                {
                    case "External":
                        if (_plugin.runAtStartup && !_runningPlugins.Contains(_plugin.id))
                        {
                            Thread extThread = new Thread(startExternalPlugin);
                            extThread.Start(_plugin);
                        } else if (_plugin.startTimeEPOC <= getEPOCH() && !_runningPlugins.Contains(_plugin.id))
                        {
                            _logging.log("pluginTWINcallback() " + _plugin.id +  " scheduled time " + _plugin.startTimeEPOC + " <= current time " + getEPOCH());
                            Thread extThread = new Thread(startExternalPlugin);
                            extThread.Start(_plugin);
                        } else
                        {
                            // need to schedule the startExternalPlugin in the future
                        }
                        break;
                    case "Payload":
                        if (!File.Exists(_plugin.downloadFile) || _plugin.fileSha1Hash != getSHA1(_plugin.downloadFile))
                        {
                            if (_plugin.startTimeEPOC == 0 || _plugin.startTimeEPOC <= getEPOCH())
                            {
                                Thread downloadThread = new Thread(downloadPayload);
                                downloadThread.Start(_plugin);
                            } else
                            {
                                // need to schedule the downloadPaylod in the future
                            }
                        }
                        break;
                    case "Code":
                        if (_plugin.runAtStartup && !_runningPlugins.Contains(_plugin.id))
                        {
                            startCodePlugin(_plugin);
                        }
                        else if (_plugin.startTimeEPOC >= getEPOCH())
                        {
                            startCodePlugin(_plugin);
                        }
                        break;
                    default:
                        _logging.log("Error with plugin: " + pluginObject.ToString(), error);
                        break;
                }
            }

            return Task.CompletedTask;
        }

        private void startCodePlugin(plugin _plugin)
        {
            _logging.log("startCodePlugin for " + _plugin.name + " " + _plugin.id.ToString());

            getDependency(_plugin);

            if (_plugin.fileSha1Hash != null && _plugin.fileSha1Hash != getSHA1(_plugin.codeFileName))
            {
                _logging.log("startCodePlugin Error: codeFileName " + _plugin.command + " Sha1 is " + getSHA1(_plugin.codeFileName), error);
                _logging.log("startCodePlugin Error: codeFileName " + _plugin.command + " Sha1 expected " + _plugin.fileSha1Hash, error);
                _logging.log("startCodePlugin Exiting for " + _plugin.id.ToString(), error);
                return;
            }

            try
            {
                Assembly assembly = Assembly.LoadFrom(_plugin.codeFileName);
                MethodInfo method = assembly.GetTypes()[0].GetMethod("Init");       // need to work on passing the DeviceClient object in reflection
                method.Invoke(this, null);
            }
            catch (Exception er)
            {
                _logging.log("Error with startCodePlugin " + er.ToString(), error);
            }
        }

        private void downloadPayload(object obj)
        {
            plugin plugin = (plugin)obj;
            downloadPayload(plugin);
        }

        private bool downloadPayload(plugin plugin)
        {
            _logging.log("Downloading " + plugin.downloadFileURL + " to file " + plugin.downloadFile);
            // future iterations should consider: https://www.codeproject.com/script/Articles/ViewDownloads.aspx?aid=18243
            WebClient webClient = new WebClient();
            webClient.DownloadFile(new System.Uri(plugin.downloadFileURL), plugin.downloadFile);
            webClient.Dispose();

            if (plugin.fileSha1Hash == getSHA1(plugin.downloadFile))
            {
                _logging.log("downloadPayload Success: " + plugin.downloadFile + " SHA1 " + getSHA1(plugin.downloadFile) + " == " + plugin.fileSha1Hash);

                // if there is a post command, like unzip
                if (plugin.postDownloadCommand != null)
                {
                    ProcessStartInfo process = new ProcessStartInfo();
                    process.FileName = plugin.postDownloadCommand.Split(" ")[0];
                    process.Arguments = plugin.postDownloadCommand.Substring(plugin.postDownloadCommand.Split(" ")[0].Length);
                    process.RedirectStandardOutput = true;
                    process.CreateNoWindow = true;
                    Process postCommand = Process.Start(process);
                    postCommand.WaitForExit();
                    _logging.log("downloadPayload Executed command: " + plugin.postDownloadCommand.Split(" ")[0] + " with parameters " + plugin.postDownloadCommand.Substring(plugin.postDownloadCommand.Split(" ")[0].Length));
                    _logging.log("downloadPayload Exit Code: " + postCommand.ExitCode.ToString());
                }

                return true;
            } else
            {
                _logging.log("downloadPayload Error: " + plugin.downloadFile + " SHA1 " + getSHA1(plugin.downloadFile) + " != " + plugin.fileSha1Hash, error);
                return false;
            }
        }

        private void getDependency(plugin _plugin)
        {
            plugin payLoad = null;
            if (_plugin.payloadDependency != new Guid())
            {
                // check if the payload had been received, else try to receive it, else error out
                JObject payloadPlugin = _twin.Descendants().OfType<JObject>().Where(x => x["id"].ToObject<Guid>() == _plugin.payloadDependency).FirstOrDefault();
                if (payloadPlugin != null)
                {
                    payLoad = JsonConvert.DeserializeObject<plugin>(payloadPlugin.ToString());
                }

                // process nested PayloadDependencies
                getDependency(payLoad);

                // we don't have the payloadDependency defined in our TWIN
                if (payLoad == null)
                {
                    _logging.log("getDependency Error: dependency " + _plugin.payloadDependency.ToString() + " not found for plugin " + _plugin.id.ToString(), error);
                    _logging.log("getDependency Exiting for " + _plugin.id.ToString(), error);
                    return;
                }

                // we either don't have the file or the SHA1 does not match, try to download it
                else if (!File.Exists(payLoad.downloadFile) || payLoad.fileSha1Hash != getSHA1(payLoad.downloadFile))
                {
                    if (!downloadPayload(payLoad))
                    {
                        _logging.log("getDependency Error: dependency " + _plugin.payloadDependency.ToString() + " not found for plugin " + _plugin.id.ToString(), error);
                        _logging.log("getDependency Exiting for " + _plugin.id.ToString(), error);
                        return;
                    }
                }
            }
        }

        private void startExternalPlugin(object obj)
        {
            plugin _plugin = (plugin)obj;

            _logging.log("startExternalPlugin for " + _plugin.name + " " +  _plugin.id.ToString());

            getDependency(_plugin);

            if (_plugin.fileSha1Hash != null && _plugin.fileSha1Hash != getSHA1(_plugin.command))
            {
                _logging.log("startExternalPlugin Error: Command " + _plugin.command + " Sha1 is "  + getSHA1(_plugin.command), error);
                _logging.log("startExternalPlugin Error: Command " + _plugin.command + " Sha1 expected " + _plugin.fileSha1Hash, error);
                _logging.log("startExternalPlugin Exiting for " + _plugin.id.ToString(), error);
                return;
            }

            if (_plugin.stdOutToMessage != null)
            {
                File.Delete(_plugin.stdOutToMessage);
            }

            if (_plugin.stdOutToTWIN != null)
            {
                File.Delete(_plugin.stdOutToTWIN);
            }

            Process startedPlugin = new Process();
            startedPlugin.StartInfo.FileName = _plugin.command;
            startedPlugin.StartInfo.Arguments = _plugin.parameters;
            startedPlugin.StartInfo.RedirectStandardOutput = true;
            startedPlugin.StartInfo.RedirectStandardError = true;
            startedPlugin.StartInfo.CreateNoWindow = true;
            startedPlugin.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                _logging.log("startExternalPlugin for " + _plugin.name + " " + _plugin.id.ToString() + " output: " + e.Data);
                if (_plugin.stdOutToMessage != null) {
                    File.AppendAllText(_plugin.stdOutToMessage, e.Data + Environment.NewLine, Encoding.UTF8);
                }

                if (_plugin.stdOutToTWIN != null)
                {
                    File.AppendAllText(_plugin.stdOutToTWIN, e.Data + Environment.NewLine, Encoding.UTF8);
                }
            });
            startedPlugin.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                _logging.log("startExternalPlugin for " + _plugin.name + " " + _plugin.id.ToString() + " Error: " + e.Data, error);
            });
            startedPlugin.Exited += new EventHandler(async (sender, e) =>
            {
                if (startedPlugin.ExitCode != 0 && _plugin.nonZeroExitPlugin != null)
                {
                    _logging.log("startExternalPlugin for " + _plugin.name + " " + _plugin.id.ToString() + " Exit Code: " + startedPlugin.ExitCode, error);
                    JObject chainedPlugin = _twin.Descendants().OfType<JObject>().Where(x => x["id"].ToObject<Guid>() == _plugin.nonZeroExitPlugin).FirstOrDefault();
                    if (chainedPlugin != null) {
                        _logging.log("startExternalPlugin for " + _plugin.name + " " + _plugin.id.ToString() + " Exit Code: " + startedPlugin.ExitCode + " sending to " + _plugin.nonZeroExitPlugin, error);
                        plugin errorPlugin = JsonConvert.DeserializeObject<plugin>(chainedPlugin.ToString());
                        startExternalPlugin(errorPlugin);
                    }
                } else if (startedPlugin.ExitCode != 0)
                {
                    _logging.log("startExternalPlugin for " + _plugin.name + " " + _plugin.id.ToString() + " Exit Code: " + startedPlugin.ExitCode + " no 'nonZeroExitPlugin' setting" , error);
                }

                if (_plugin.stdOutToTWIN != null)
                {
                    try
                    {
                        JObject twinData = new JObject();
                        twinData[_plugin.id.ToString()] = JObject.Parse(File.ReadAllText(_plugin.stdOutToTWIN));
                        TwinCollection twinCollection = new TwinCollection(twinData.ToString());
                        await _deviceClient.UpdateReportedPropertiesAsync(twinCollection);
                    }
                    catch (Exception er)
                    {
                        _logging.log("startExternalPlugin for " + _plugin.name + " " + _plugin.id.ToString() + " TWIN Error: " + er.ToString(), error);
                    }
                }

                // if we are not configured to run at startup, then we only run once (do not restart)
                if (_plugin.runAtStartup == true)
                {
                    _runningPlugins.Remove(_plugin.id);
                }
            });

            startedPlugin.Start();
            startedPlugin.BeginErrorReadLine();
            startedPlugin.BeginOutputReadLine();
            _runningPlugins.Add(_plugin.id);
            startedPlugin.WaitForExit();
            _logging.log("ended ExternalPlugin for " + _plugin.name + " " + _plugin.id.ToString() + " " + _plugin.command + " with exit code " + startedPlugin.ExitCode);
        }

        public string getSHA1(string filename)
        {
            try
            {
                FileStream fs = File.OpenRead(filename);
                string hash = Convert.ToBase64String(sha1.ComputeHash(fs));
                fs.Close();
                return hash;
            }
            catch (Exception er)
            {
                _logging.log("Error with getSHA1 " + er.ToString(), error);
                return "";
            }
        }

        public int getEPOCH()
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return (int)t.TotalSeconds;
        }

        public class plugin
        {
            public string name { get; set; }
            public Guid id { get; set; }
            public string type { get; set; }                // External, Payload or Code
            public Guid nonZeroExitPlugin { get; set; }     // optional
            public Guid payloadDependency { get; set; }     // optional
            public bool runAtStartup { get; set; }          // not used for Payload
            public int startTimeEPOC { get; set; }
            public string fileSha1Hash { get; set; }

            // unique to External
            public string command { get; set; }
            public string parameters { get; set; }
            public string stdOutToMessage { get; set; }
            public string stdOutToTWIN { get; set; }
            public string stdInTWIN { get; set; }
            
            // unique to Payload
            public int downloadBandwidthRate { get; set; }
	        public string downloadFileURL { get; set; }
            public string downloadFile { get; set; }
            public string postDownloadCommand { get; set; }

            // unique to type Code
            public string codeFileName { get; set; }
            
        }
    }
}
