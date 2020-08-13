using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AzureIoTAgent
{
    class RemoteStream
    {
        static CommonLogging _logging;
        static DeviceClient _deviceClient;
        static int _targetPort;
        static string _targetHost;

        public RemoteStream(DeviceClient deviceClient, JObject config, CommonLogging logging)
        {
            _logging = logging;
            _logging.log("Starting RemoteStream");
            _deviceClient = deviceClient;
            _targetHost = "localhost";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _targetPort = 22;
            }
            else
            {
                _targetPort = 3389;
            }

        }

        public async Task DeviceStreamListenForever(CancellationTokenSource cancellationTokenSource)
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await DeviceStreamListen(cancellationTokenSource).ConfigureAwait(false);
                }
                catch (Exception)
                {

                }
            }
        }

        public async Task DeviceStreamListen(CancellationTokenSource cancellationTokenSource)
        {
            _logging.log("RemoteStream listenting to connect to " + _targetPort);
            DeviceStreamRequest streamRequest = await _deviceClient.WaitForDeviceStreamRequestAsync(cancellationTokenSource.Token).ConfigureAwait(false);
            if (streamRequest != null)
            {
                await _deviceClient.AcceptDeviceStreamRequestAsync(streamRequest, cancellationTokenSource.Token).ConfigureAwait(false);

                using (ClientWebSocket webSocket = await DeviceStreamingCommon.GetStreamingClientAsync(streamRequest.Url, streamRequest.AuthorizationToken, cancellationTokenSource.Token).ConfigureAwait(false))
                {
                    using (TcpClient tcpClient = new TcpClient())
                    {
                        await tcpClient.ConnectAsync(_targetHost, _targetPort).ConfigureAwait(false);

                        using (NetworkStream localStream = tcpClient.GetStream())
                        {
                            _logging.log("Streaming started to " + _targetHost + ":" + _targetPort);

                            await Task.WhenAny(
                                HandleIncomingDataAsync(localStream, webSocket, cancellationTokenSource.Token),
                                HandleOutgoingDataAsync(localStream, webSocket, cancellationTokenSource.Token)).ConfigureAwait(false);

                            localStream.Close();

                            _logging.log("Streaming closed to " + _targetHost + ":" + _targetPort);
                        }
                    }

                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, String.Empty, cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
        }

        private static async Task HandleIncomingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (remoteStream.State == WebSocketState.Open)
            {
                var receiveResult = await remoteStream.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                await localStream.WriteAsync(buffer, 0, receiveResult.Count).ConfigureAwait(false);
            }
        }

        private static async Task HandleOutgoingDataAsync(NetworkStream localStream, ClientWebSocket remoteStream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[10240];

            while (localStream.CanRead)
            {
                int receiveCount = await localStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                await remoteStream.SendAsync(new ArraySegment<byte>(buffer, 0, receiveCount), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            }
        }

    }
    public static class DeviceStreamingCommon
    {
        /// <summary>
        /// Creates a ClientWebSocket with the proper authorization header for Device Streaming.
        /// </summary>
        /// <param name="url">Url to the Streaming Gateway.</param>
        /// <param name="authorizationToken">Authorization token to connect to the Streaming Gateway.</param>
        /// <param name="cancellationToken">The token used for cancelling this operation if desired.</param>
        /// <returns>A ClientWebSocket instance connected to the Device Streaming gateway, if successful.</returns>
        public static async Task<ClientWebSocket> GetStreamingClientAsync(Uri url, string authorizationToken, CancellationToken cancellationToken)
        {
            ClientWebSocket wsClient = new ClientWebSocket();
            wsClient.Options.SetRequestHeader("Authorization", "Bearer " + authorizationToken);

            await wsClient.ConnectAsync(url, cancellationToken).ConfigureAwait(false);

            return wsClient;
        }
    }
}
