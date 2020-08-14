# Project IoT Agent
## Objectives:
Project IoT Agent is an Open Source project to address Operational Management challenges of IoT Devices, including both Azure IoT Edge and Azure IoT Device Client (non IoT Edge) solutions.  IoT Agent addresses the gaps created when operationalizing and deploying IoT solutions.  Individual products like IoT Edge solve discrete needs, but do not address complete solutions needs, needs identified and crated when integrating and deploying many products into a full manageable solution.  Examples of these needs include deployment, monitoring, maintaining, updating and troubleshooting IoT device/gateways in the field, where devices are remote and not readily accessible.
While addressing Operational Management, the focus of IoT Agent is to not overlap or recreate capability addressed by other components, specifically Security Center, IoT Edge, Device Update Center or Azure Device Update, as shown to the right.
## Goals:
### Goals include:
Operating System Management and Monitoring
* Configuration Management or Device and OS
* Certificate Management
* Hardware Administration and Management
* Remote Management
* Remote Logging

## Areas
IoT Agent has been defined with 4 major components including Base Agent, Certificate Management, Remote Streaming and Logging and Plugin Framework, as shown:
 
### Common Criteria:
Unless noted differently, below is the defined common criteria for IoT Agent:
1. All code is written in C# on .Net Core version 3.1
2. All binaries are compiled as a self-contained, single binary and OS Specific (does not require the .Net Framework to be installed)
3. All messaging will be facilitated via the IoT Client SDK, leveraging one or more Azure IoT Hub for bi direction communication
4. IoT Hub Device Streams https://docs.microsoft.com/en-us/azure/iot-hub/iot-hub-device-streams-overview (aka Device Streams) will facilitate remote streaming capability.
5. Both x.509 Certificate and Symmetric Key will be enabled for authentication, leveraging Azure Device Provisioning Service for provisioning
6. Windows 10 x64 and Ubuntu 18.04 Linux will be tested platforms

### Base IoT Agent Objectives and Requirements:
* IoT Agent will facilitate Plugins as defined in the Plugin Framework section
* IoT Agent will authenticate to Azure IoT Hub, via DPS
* IoT Agent can leverage the same IoT Hub used by IoT Edge (authenticating as a Module) or can use a separate IoT Hub for risk mitigation
* IoT Agent will inventory and report via TWIN:
  * CPU count and description
   * Total Memory
	 * Total Disk Size and current used disk space
	 * BIOS version
	 * Hardware temperature, where applicable
  * If a defined list of processes are running
  * Will update the Device TWIN and/or send a heartbeat message
  * Will have the ability to auto update itself
### Deployment of IoT Agent can be generalized
### Certificate Management Objectives and Requirements:
 * Certificate operations will be limited to:
  * Issuing Certificates
  * Renewing Certificates
  * Inventorying Certificates
 * Certificate Inventory will be facilitated via Device TWIN and will include:
  * Last Inventory DateTime
  * Serial Number
  * Issuing Authority
  * Expiration Date
  * CN
 * Certificates will be inventoried on a periodic (configurable) basis
 * On Linux, Certificates paths will be defined (example /var/lib/iotedge/hsm/certs/mycert.pem)
 * On Windows, only Machine Certificates will be included
 * One sample “Certificate Renewal” endpoint, such as a REST endpoint will be demonstrated
 * There are no hard dependency on a top level certificate provider or single certificate service solution (example Windows Certificate Service)
 * Certificates Management can be implemented as a Plugin
### Plugin Framework Objectives and Requirements:
 * Plugins will follow 3 patterns, Code, Payload or External, where Code is a C# Class Library, Payload is content and External is an external binary with optional payload, such as ‘c:\program~1\python.exe’ and ‘myscript.py’.
 * IoT Agent will launch the plugin in a separate thread and monitor the execution
 * Plugins will not start if dependent Payload files have not been executed
 * Payload Plugin will use a publicly accessible URL (with optional SAS Token) and will leverage the method GET to download the file.
 * All Plugins will be defined as TWIN properties under the “Plugin” object.
 * Payloads can be scheduled to download and throttled to preserve bandwidth
 * Plugins will all follow a common schema, such as (but not limited to):
```
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
```
