using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Provisioning.Security;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace AzureIoTAgent
{
    class Authenticate
    {
        static CommonLogging _logging;
        static DeviceClient deviceClient;       // want to support ModuleClient also

        public Authenticate(CommonLogging logging)
        {
            _logging = logging;
        }
        
        public async Task<DeviceClient> AuthenticateWithDPSandTPM(string scopeID, string registrationId, string endpoint, TransportType transportType, ProvisioningTransportHandler provisioningTransport)
        {
            _logging.log(String.Format("AuthenticateWithTPM({0}, {1}, {2}, {3}, {4})", scopeID, registrationId, endpoint, transportType.ToString(), provisioningTransport.ToString()));

            try
            {
                SecurityProviderTpm tpm = new SecurityProviderTpmHsm(registrationId: registrationId);

                _logging.log(" EndorsementKey found: " + Convert.ToBase64String(tpm.GetEndorsementKey()));

                ProvisioningDeviceClient provisioningDevice = ProvisioningDeviceClient.Create(endpoint, scopeID, tpm, provisioningTransport);
                DeviceRegistrationResult registrationResult = await provisioningDevice.RegisterAsync().ConfigureAwait(false);

                DeviceAuthenticationWithTpm auth = new DeviceAuthenticationWithTpm(registrationResult.DeviceId, tpm);

                _logging.log(String.Format(" registrationResult = {0}", registrationResult.Status));

                deviceClient = DeviceClient.Create(registrationResult.AssignedHub, auth, transportType);
            }
            catch (Exception error)
            {
                _logging.log(String.Format("AuthenticateWithTPM() Error: {0}", error.ToString()), 1);
                return null;
            }
            
            return deviceClient;
        }

        public async Task<DeviceClient> AuthenticateWithDPSandx509(string scopeID, string pfxFilename, string endpoint, TransportType transportType,  ProvisioningTransportHandler provisioningTransport)
        {
            _logging.log(String.Format("AuthenticateWithDPSandx509({0}, {1}, {2}, {3}, {4})", scopeID, pfxFilename, endpoint, transportType.ToString(),  provisioningTransport.ToString()));

            try
            {
                X509Certificate2 certificate = new X509Certificate2(pfxFilename);

                _logging.log(" Certificate Loaded: " + certificate.Thumbprint.ToString());

                SecurityProviderX509Certificate cert = new SecurityProviderX509Certificate(certificate);
                ProvisioningDeviceClient provisioningDevice = ProvisioningDeviceClient.Create(endpoint, scopeID, cert, provisioningTransport);
                DeviceRegistrationResult registrationResult = await provisioningDevice.RegisterAsync().ConfigureAwait(false);

                _logging.log(String.Format(" registrationResult = {0}", registrationResult.Status));

                DeviceAuthenticationWithX509Certificate auth = new DeviceAuthenticationWithX509Certificate(registrationResult.DeviceId, certificate);

                deviceClient = DeviceClient.Create(registrationResult.AssignedHub, auth, transportType);
            }
            catch (Exception error)
            {
                _logging.log(String.Format("AuthenticateWithDPSandx509() Error: {0}", error.ToString()), 1);
                return null;
            }

            return deviceClient;
        }

        public async Task<DeviceClient> AuthenticateWithConnectionString(string connectionString, TransportType transportType = TransportType.Amqp)
        {
            _logging.log(String.Format("AuthenticateWithConnectionString({0}, {1})", connectionString, transportType.ToString()));

            try
            {
                if (connectionString.Contains("ModuleId="))
                {
                    //deviceClient = ModuleClient.CreateFromConnectionString(connectionString, transportType);
                } else
                {
                    deviceClient = DeviceClient.CreateFromConnectionString(connectionString, transportType);
                }
            }
            catch (Exception error)
            {
                _logging.log(String.Format("AuthenticateWithConnectionString() Error: {0}", error.ToString()), 1);
                return null;
            }

            return deviceClient;
        }
    }
}
