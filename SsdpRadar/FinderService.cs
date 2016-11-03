using SsdpRadar;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace SsdpRadar
{
   public class FinderService : IFinderService
   {

      // Used for sending on socket
      private const int SSDP_MULTICAST_PORT = 1900;

      // Used for receiving on socket
      private const int SSDP_UNICAST_PORT = 1901;

      private const string SSDP_ADDR = "239.255.255.250";
      private static readonly IPAddress SSDP_IP = IPAddress.Parse(SSDP_ADDR);

      // Endpoint sent to
      private static readonly EndPoint SSDP_MULTICAST_ENDPOINT = new IPEndPoint(SSDP_IP, SSDP_MULTICAST_PORT);
      private static readonly EndPoint SSDP_RECEIVE_ENDPOINT = new IPEndPoint(IPAddress.Any, SSDP_MULTICAST_PORT);

      //ConcurrentDictionary<string, SsdpDevice> _deviceCache = new ConcurrentDictionary<string, SsdpDevice>();
      ConcurrentDictionary<Uri, SsdpDevice> _foundLocations = new ConcurrentDictionary<Uri, SsdpDevice>();

      TaskCompletionSource<object> _cancelTask = new TaskCompletionSource<object>();
      bool _isCancelled => _cancelTokenSrc.IsCancellationRequested;
      CancellationTokenSource _cancelTokenSrc;
      TimeSpan _rebroadcastInterval;
      TimeSpan _replyWait;
      Action<SsdpDevice> _deviceFoundCallback = null;

      int _broadcasts;

      DateTime _startedTime;

      public FinderService(int broadcasts, TimeSpan rebroadcastInterval, TimeSpan replyWait, CancellationToken? cancelToken = null)
      {
         _replyWait = replyWait;
         _rebroadcastInterval = rebroadcastInterval;
         _broadcasts = broadcasts;
         _cancelTokenSrc = new CancellationTokenSource();
         if (cancelToken != null)
         {
            cancelToken.Value.Register(_cancelTokenSrc.Cancel);
         }
         _cancelTokenSrc.Token.Register(() => _cancelTask.TrySetCanceled());
      }

      public static BufferBlock<SsdpDevice> StreamDevices(int broadcasts, TimeSpan rebroadcastInterval, TimeSpan replyWait, CancellationToken? cancelToken = null)
      {
         var finderServer = new FinderService(broadcasts, rebroadcastInterval, replyWait, cancelToken);
         var bufferBlock = finderServer.StreamDevices();
         bufferBlock.Completion.ContinueWith(t => finderServer.Dispose());
         return bufferBlock;
      }

      public BufferBlock<SsdpDevice> StreamDevices()
      {
         var bufferBlock = new BufferBlock<SsdpDevice>();

         _deviceFoundCallback = d => bufferBlock.Post(d);
         var broadcastTask = BroadcastSockets();

         broadcastTask.ContinueWith(t => bufferBlock.Complete());
         _cancelTokenSrc.Token.Register(() => bufferBlock.Complete());

         return bufferBlock;
      }

      public async Task<IEnumerable<SsdpDevice>> FindDevicesAsync(Action<SsdpDevice> deviceFoundCallback = null)
      {
         List<SsdpDevice> devices = new List<SsdpDevice>();
         _deviceFoundCallback = d =>
         {
            lock (devices)
            {
               devices.Add(d);
            }
            deviceFoundCallback?.Invoke(d);
         };
         await BroadcastSockets();
         return devices;
      }

      class NetworkInterfaceInfo
      {
         public int InterfaceIndex { get; private set; }
         public IPAddress IPAddress { get; private set; }
         public NetworkInterface NetworkInterface { get; private set; }

         public NetworkInterfaceInfo(NetworkInterface ni, int index, IPAddress ipAddress)
         {
            NetworkInterface = ni;
            InterfaceIndex = index;
            IPAddress = ipAddress;
         }
      }

      IEnumerable<NetworkInterfaceInfo> GetUsableNetworkInterfaces()
      {
         foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
         {
            //if (!adapter.GetIPProperties().MulticastAddresses.Any())
            //   continue; // most of VPN adapters will be skipped

            if (!adapter.SupportsMulticast)
               continue; // multicast is meaningless for this type of connection

            if (OperationalStatus.Up != adapter.OperationalStatus)
               continue; // this adapter is off or not connected

            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
               continue; // strip out loopback addresses



            int interfaceIndex = -1;
            var ipProps = adapter.GetIPProperties();
            try
            {
               interfaceIndex = ipProps.GetIPv4Properties().Index;
            }
            catch
            {
               try
               {
                  interfaceIndex = ipProps.GetIPv6Properties().Index;
               }
               catch
               {
                  // failed to get ipv4 of ipv6 properties..
                  continue;
               }
            }

            var ipAddress = ipProps.UnicastAddresses
               .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
               .Concat(ipProps.UnicastAddresses.Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetworkV6))
               .FirstOrDefault()?
               .Address;

            if (ipAddress == null)
               continue; // could not find an IPv4 or IPv6 address for this adapter

            if (ipAddress.IsIPv6LinkLocal)
               continue;

            yield return new NetworkInterfaceInfo(adapter, interfaceIndex, ipAddress);
         }
      }

      async Task BroadcastSockets()
      {
         _startedTime = DateTime.UtcNow;

         var niIndexs = GetUsableNetworkInterfaces();

         var socketTasks = niIndexs.Select(a => BroadcastSocket(a)).ToList();

         await Task.WhenAll(socketTasks);
      }

      async Task BroadcastSocket(NetworkInterfaceInfo adapter)
      {
         using (var udpClient = new UdpClient(adapter.IPAddress.AddressFamily))
         {
            var socket = udpClient.Client;

            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.HostToNetworkOrder(adapter.InterfaceIndex));

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            udpClient.ExclusiveAddressUse = false;

            socket.Bind(new IPEndPoint(adapter.IPAddress, SSDP_UNICAST_PORT));

            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(SSDP_IP, adapter.InterfaceIndex));

            var receiveTask = ReceiveServicer(udpClient);
            var broadcastTask = BroadcastServicer(udpClient);
            await Task.WhenAll(receiveTask, broadcastTask);

            udpClient.Close();
         }
      }

      private async Task BroadcastServicer(UdpClient client)
      {
         var broadcastString = string.Join("\r\n",
            "M-SEARCH * HTTP/1.1",
            "Host:" + SSDP_ADDR + ":" + SSDP_MULTICAST_PORT,
            "Man:\"ssdp:discover\"",
            "ST:ssdp:all",
            "MX:3",
            "\r\n\r\n"
         );

         var broadcastData = Encoding.ASCII.GetBytes(broadcastString);

         int broadcastsDone = 0;

         while (!_isCancelled && (_broadcasts < 1 || broadcastsDone < _broadcasts))
         {
            try
            {
               var asyncResult = await client.SendAsync(broadcastData, broadcastData.Length, (IPEndPoint)SSDP_MULTICAST_ENDPOINT);

               if (_broadcasts > 0)
               {
                  broadcastsDone++;
               }

               if (_isCancelled)
               {
                  return;
               }
               if (_rebroadcastInterval.TotalMilliseconds > 0)
               {
                  await Task.WhenAny(Task.Delay(_rebroadcastInterval), _cancelTask.Task);
               }
            }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
               Console.WriteLine(ex);
            }
         }

      }

      async void ObserveUdpReadTask(Task task)
      {
         try
         {
            await task;
         }
         catch (ObjectDisposedException) { }
         catch (OperationCanceledException) { }
         catch (Exception ex)
         {
            Console.WriteLine(ex);
#if DEBUG
            throw;
#endif
         }
      }

      async Task<UdpReceiveResult> SafeReceiveAsync(UdpClient client)
      {
         try
         {
            return await client.ReceiveAsync();
         }
         catch
         {
            return new UdpReceiveResult();
         }
      }

      private async Task ReceiveServicer(UdpClient client)
      {
         var endpoint = SSDP_RECEIVE_ENDPOINT;

         var replyWaitTask = _replyWait.TotalMilliseconds > 0 ? Task.Delay(_replyWait) : new TaskCompletionSource<object>().Task;

         List<Task> fetchDeviceInfoTasks = new List<Task>();

         while (!_isCancelled && !replyWaitTask.IsCompleted)
         {
            try
            {
               var receiveTask = SafeReceiveAsync(client);
               var finishedTask = await Task.WhenAny(receiveTask, _cancelTask.Task, replyWaitTask);

               if (finishedTask == receiveTask)
               {
                  var asyncResult = await receiveTask;
                  if (asyncResult.Buffer == null)
                  {
                     continue;
                  }
                  var received = receiveTask.Result;
                  if (received.Buffer != null && received.Buffer.Length > 0)
                  {
                     var responseData = Encoding.ASCII.GetString(received.Buffer, 0, received.Buffer.Length);

                     var device = SsdpDevice.ParseBroadcastResponse(received.RemoteEndPoint.Address, responseData);
                     if (device != null)
                     {
                        if (_foundLocations.TryAdd(device.Location, device))
                        {
                           if (device.Location.Scheme != "unknown")
                           {
                              fetchDeviceInfoTasks.Add(FetchDeviceInfo(device, _cancelTokenSrc.Token));
                           }
                           else
                           {
                              _deviceFoundCallback?.Invoke(device);
                           }
                        }
                     }
                  }
               }
               else
               {
                  ObserveUdpReadTask(receiveTask);
               }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
               Console.WriteLine(ex);
#if DEBUG
               throw ex;
#endif
            }
         }

         if (fetchDeviceInfoTasks.Count > 0)
         {
            await Task.WhenAll(fetchDeviceInfoTasks);
         }
      }

      private async Task FetchDeviceInfo(SsdpDevice device, CancellationToken cancelToken)
      {
         try
         {
            //var response = await _httpClient.GetAsync(device.Location, cancelToken);
            var httpRequest = WebRequest.CreateHttp(device.Location);
            httpRequest.Timeout = (int)Math.Round(_replyWait.TotalMilliseconds);
            cancelToken.Register(() => httpRequest.Abort());

            using (var response = (HttpWebResponse)(await httpRequest.GetResponseAsync()))
            {
               if (response.StatusCode == HttpStatusCode.OK)
               {
                  using (var responseStream = response.GetResponseStream())
                  using (var responseReader = new StreamReader(responseStream, Encoding.UTF8))
                  {
                     var data = await responseReader.ReadToEndAsync();
                     var deviceInfo = SsdpDeviceInfo.ParseDeviceResponse(data);
                     device.Info = deviceInfo;
                  }
               }
            }
         }
         catch (ArgumentException) { }
         catch (WebException) { }
         catch (ObjectDisposedException) { }
         catch (OperationCanceledException) { }
         catch (Exception ex)
         {
            Console.WriteLine(ex);
         }

         _deviceFoundCallback?.Invoke(device);
      }

      public void Dispose()
      {
         _cancelTokenSrc.Cancel();
         _cancelTask.TrySetCanceled();
         _deviceFoundCallback = null;
      }
   }

}
