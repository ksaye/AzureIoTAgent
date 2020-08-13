using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace AzureIoTAgent
{
    class CertificateManagement
    {
        static CommonLogging _logging;
        static DeviceClient _deviceClient;

        public CertificateManagement(DeviceClient deviceClient, JObject config, CommonLogging logging)
        {
            _logging = logging;
            _logging.log("Starting CertificateManagement");
            _deviceClient = deviceClient;

            // need to add thread to monitor and update certificates
            // we have access to deviceClient here
        }
        
        internal Task ProcessCertificatestForever(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

}
