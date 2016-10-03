using SsdpRadar;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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


      HttpClient _httpClient;

      //ConcurrentDictionary<string, SsdpDevice> _deviceCache = new ConcurrentDictionary<string, SsdpDevice>();
      ConcurrentDictionary<Uri, SsdpDevice> _foundLocations = new ConcurrentDictionary<Uri, SsdpDevice>();

      TaskCompletionSource<object> _cancelTask = new TaskCompletionSource<object>();
      bool _isCancelled => _cancelTask.Task.IsCanceled;
      bool _isStarted;
      TimeSpan _rebroadcastInterval;
      TimeSpan _replyWait;
      Action<SsdpDevice> _deviceFoundCallback = null;

      int _broadcasts;

      DateTime _startedTime;

      public FinderService(int broadcasts, TimeSpan rebroadcastInterval, TimeSpan replyWait, HttpClient httpClient = null, CancellationToken cancelToken = default(CancellationToken))
      {
         _replyWait = replyWait;
         _rebroadcastInterval = rebroadcastInterval;
         _broadcasts = broadcasts;
         if (httpClient == null)
         {
            _httpClient = new HttpClient();
            _httpClient.Timeout = replyWait;
         }
         else
         {
            _httpClient = httpClient;
         }
         cancelToken.Register(() => _cancelTask.TrySetCanceled());
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

      async void StartSetupSockets()
      {
         await BroadcastSockets();
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
            if (!adapter.GetIPProperties().MulticastAddresses.Any())
               continue; // most of VPN adapters will be skipped

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

            yield return new NetworkInterfaceInfo(adapter, interfaceIndex, ipAddress);
         }
      }

      async Task BroadcastSockets()
      {
         _startedTime = DateTime.UtcNow;

         //var localAddresses = _networkInterfaceProvider.GetLocalInterfaces().Select(b => new IPAddress(b));

         var niIndexs = GetUsableNetworkInterfaces();

         var socketTasks = niIndexs.Select(a => BroadcastSocket(a));

         await Task.WhenAny(_cancelTask.Task, Task.WhenAll(socketTasks));
      }

      async Task BroadcastSocket(NetworkInterfaceInfo adapter)
      {
         using (var socket = new Socket(adapter.IPAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
         {
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.HostToNetworkOrder(adapter.InterfaceIndex));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(SSDP_IP, adapter.InterfaceIndex));
            socket.Bind(new IPEndPoint(adapter.IPAddress, SSDP_UNICAST_PORT));

            var receiveTask = ReceiveServicer(socket);
            var broadcastTask = BroadcastServicer(socket);

            await Task.WhenAny(_cancelTask.Task, Task.WhenAll(receiveTask, broadcastTask));
         }
      }

      private async Task BroadcastServicer(Socket socket)
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
               var asyncResult = await socket.SendToAsync(new ArraySegment<byte>(broadcastData), SocketFlags.None, SSDP_MULTICAST_ENDPOINT);

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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
               System.Console.WriteLine(ex);
            }
         }

      }

      private async Task ReceiveServicer(Socket socket)
      {
         var endpoint = SSDP_RECEIVE_ENDPOINT;

         var replyWaitTask = _replyWait.TotalMilliseconds > 0 ? Task.Delay(_replyWait) : new TaskCompletionSource<object>().Task;

         List<Task> fetchDeviceInfoTasks = new List<Task>();

         while (!_isCancelled && !replyWaitTask.IsCompleted)
         {
            try
            {
               var buffer = new byte[ushort.MaxValue];

               var receiveTask = socket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, endpoint);
               var finishedTask = await Task.WhenAny(receiveTask, _cancelTask.Task, replyWaitTask);

               if (finishedTask == receiveTask)
               {
                  var asyncResult = await receiveTask;
                  var received = receiveTask.Result;
                  if (received.ReceivedBytes > 0)
                  {
                     var responseData = Encoding.ASCII.GetString(buffer, 0, received.ReceivedBytes);
                     var device = SsdpDevice.ParseBroadcastResponse(responseData);
                     if (device != null)
                     {
                        if (_foundLocations.TryAdd(device.Location, device))
                        {
                           if (device.Location.Scheme != "unknown")
                           {
                              fetchDeviceInfoTasks.Add(FetchDeviceInfo(device));
                           }
                           else
                           {
                              _deviceFoundCallback?.Invoke(device);
                           }
                        }
                     }
                  }
               }

            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
               System.Console.WriteLine(ex);
            }
         }

         if (fetchDeviceInfoTasks.Count > 0)
         {
            await Task.WhenAll(fetchDeviceInfoTasks);
         }
      }

      private async Task FetchDeviceInfo(SsdpDevice device)
      {
         try
         {
            var response = await _httpClient.GetAsync(device.Location);
            if (response.StatusCode == HttpStatusCode.OK)
            {
               var data = await response.Content.ReadAsByteArrayAsync();
               var dataStr = Encoding.UTF8.GetString(data);
               var deviceInfo = SsdpDeviceInfo.ParseDeviceResponse(dataStr);
               device.Info = deviceInfo;

               //if (!_deviceCache.TryAdd(deviceInfo.Udn, device))
               //{
               //   return;
               //}
            }
         }
         catch (OperationCanceledException) { }
         catch (Exception ex)
         {
            System.Console.WriteLine(ex);
         }

         _deviceFoundCallback?.Invoke(device);
      }

      public void Dispose()
      {
         _cancelTask.SetCanceled();
         _httpClient.Dispose();
         _deviceFoundCallback = null;
      }
   }

}
