using Microsoft.Azure.Amqp.Framing;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Azure.Devices.Client;

namespace AzureIoTAgent
{
    class CommonLogging
    {
        static JObject _config;
        static DeviceClient _deviceClient;
        static EventHubProducerClient eventHub;
        const int info = 0;
        const int error = 1;

        public CommonLogging(JObject config)
        {
            _config = config;
        }

        public void setDeviceClient(DeviceClient deviceClient)
        {
            _deviceClient = deviceClient;
        }

        public async void log(string dataToLog, int logLevel=info)
        {
            if (logLevel == info) { 
                Console.WriteLine(DateTime.Now.ToString() + " => " + dataToLog);
            } else if (logLevel == error && _config.ContainsKey("remoteLoggingConnectionString"))
            {
                Console.WriteLine(DateTime.Now.ToString() + " => " + dataToLog);
                string remoteLoggingConnectionString = _config["remoteLoggingConnectionString"].ToString();
                if (remoteLoggingConnectionString.Contains("Endpoint=sb://", StringComparison.CurrentCultureIgnoreCase)){
                    // sending messages to an Event Hub
                    eventHub = new EventHubProducerClient(remoteLoggingConnectionString);
                    EventData eventData = new EventData(Encoding.UTF8.GetBytes(dataToLog));
                    eventData.Properties.Add("hostname", Environment.MachineName);
                    EventDataBatch eventDataBatch = await eventHub.CreateBatchAsync();
                    eventDataBatch.TryAdd(eventData);
                    await eventHub.SendAsync(eventDataBatch);
                    Console.WriteLine(DateTime.Now.ToString() + " " + dataToLog + " => " + eventHub.GetType().ToString() + " " +  eventHub.EventHubName);
                }
            }
        }
    }
}
