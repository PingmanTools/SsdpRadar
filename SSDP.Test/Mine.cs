using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SSDP.Test
{
   public class SsdpFinder : IDisposable
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

      public event Action<SsdpDevice> DeviceFound;

      int found = 0;
      int uniqueLocations = 0;

      private const float BROADCAST_INTERVAL_SECONDS = 1;

      private Socket socket;
      private HttpClient httpClient;
      private TaskCompletionSource<object> servicerCancelTask;

      Dictionary<string, SsdpDevice> deviceCache;
      List<Uri> fetchedLocations;

      public SsdpFinder()
      {

      }

      public void Start()
      {
         servicerCancelTask = new TaskCompletionSource<object>();
         deviceCache = new Dictionary<string, SsdpDevice>();
         fetchedLocations = new List<Uri>();
         httpClient = new HttpClient();

         SetupSocket();
         ReceiveServicer();
         BroadcastServicer();
      }

      void SetupSocket()
      {
         socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
         socket.Bind(new IPEndPoint(IPAddress.Any, SSDP_UNICAST_PORT));
         socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(SSDP_IP, IPAddress.Any));
      }

      private async void BroadcastServicer()
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

         while (!servicerCancelTask.Task.IsCanceled)
         {
            try
            {
               var sendCompletion = new TaskCompletionSource<IAsyncResult>();

               socket.BeginSendTo(broadcastData, 0, broadcastData.Length, SocketFlags.None, SSDP_MULTICAST_ENDPOINT, r => sendCompletion.SetResult(r), null);
               await Task.WhenAny(servicerCancelTask.Task, sendCompletion.Task);
               ThrowIfServicerCancelled();

               var asyncResult = await sendCompletion.Task;
               socket.EndSendTo(asyncResult);

               await Task.WhenAny(servicerCancelTask.Task, Task.Delay(TimeSpan.FromSeconds(BROADCAST_INTERVAL_SECONDS)));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
               Console.WriteLine(ex);
            }
         }

      }

      private void ThrowIfServicerCancelled()
      {
         if (servicerCancelTask.Task.IsCanceled)
         {
            throw new OperationCanceledException();
         }
      }

      private async void ReceiveServicer()
      {
         var buffer = new byte[ushort.MaxValue];
         var endpoint = SSDP_RECEIVE_ENDPOINT;

         while (!servicerCancelTask.Task.IsCanceled)
         {
            try
            {
               var receiveCompletion = new TaskCompletionSource<IAsyncResult>();
               socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endpoint, r => receiveCompletion.SetResult(r), null);

               await Task.WhenAny(servicerCancelTask.Task, receiveCompletion.Task);
               ThrowIfServicerCancelled();
               var asyncResult = await receiveCompletion.Task;

               var received = socket.EndReceiveFrom(asyncResult, ref endpoint);
               if (received <= 0)
               {
                  continue;
               }

               var responseData = Encoding.ASCII.GetString(buffer, 0, received);
               var device = SsdpDevice.ParseBroadcastResponse(responseData);
               if (device != null)
               {
                  found++;
                  FetchDeviceInfo(device);
               }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
               Console.WriteLine(ex);
            }
         }
      }

      private async void FetchDeviceInfo(SsdpDevice device)
      {
         try
         {
            lock (fetchedLocations)
            {
               if (fetchedLocations.Contains(device.Location))
               {
                  return;
               }
               fetchedLocations.Add(device.Location);
            }

            var response = await httpClient.GetAsync(device.Location);
            if (response.StatusCode == HttpStatusCode.OK)
            {
               var data = await response.Content.ReadAsByteArrayAsync();
               var dataStr = Encoding.UTF8.GetString(data);
               var deviceInfo = SsdpDeviceInfo.ParseDeviceResponse(dataStr);
               device.Info = deviceInfo;

               lock (deviceCache)
               {
                  if (deviceCache.ContainsKey(deviceInfo.Udn))
                  {
                     return;
                  }
                  deviceCache.Add(deviceInfo.Udn, device);
                  uniqueLocations++;
               }
            }

            if (DeviceFound != null)
            {
               DeviceFound(device);
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine(ex);
         }
      }


      public void Dispose()
      {
         servicerCancelTask.SetCanceled();
      }
   }

   static class XmlExt
   {
      public static string LookupXmlKey(this XElement el, string key)
      {
         return el.Element(el.Document.Root.Name.Namespace + key)?.Value;
      }
   }

   public class SsdpServiceInfo
   {
      public string ServiceType { get; private set; }
      public string ServiceID { get; private set; }
      public string ControUrl { get; private set; }
      public string EventSubscriptionUrl { get; private set; }
      public string ServiceDescriptionUrl { get; private set; }

      public static SsdpServiceInfo[] ParseElementServices(XElement device)
      {
         return device
            .Element(device.Document.Root.Name.Namespace + "serviceList")
            .Elements(device.Document.Root.Name.Namespace + "service")
            .Select(e => new SsdpServiceInfo
            {
               ServiceType = e.LookupXmlKey("serviceType"),
               ServiceID = e.LookupXmlKey("serviceId"),
               ControUrl = e.LookupXmlKey("controlURL"),
               EventSubscriptionUrl = e.LookupXmlKey("eventSubURL"),
               ServiceDescriptionUrl = e.LookupXmlKey("SCPDURL")
            }).ToArray();
      }
   }

   public class SsdpDeviceInfo
   {
      public string DeviceType { get; private set; }
      public string FriendlyName { get; private set; }
      public string Manufacturer { get; private set; }
      public string ManufacturerUrl { get; private set; }
      public string ModelDescription { get; private set; }
      public string ModelName { get; private set; }
      public string ModelNumber { get; private set; }
      public string SerialNumber { get; private set; }
      public string Udn { get; private set; }

      public SsdpServiceInfo[] ServiceList { get; private set; }

      public string RawXml { get; private set; }

      public static SsdpDeviceInfo ParseDeviceResponse(string data)
      {
         var xDocument = XDocument.Parse(data);
         var device = xDocument.Root.Element(xDocument.Root.Name.Namespace + "device");
         return new SsdpDeviceInfo
         {
            RawXml = xDocument.ToString(),
            DeviceType = device.LookupXmlKey("deviceType"),
            FriendlyName = device.LookupXmlKey("friendlyName"),
            Manufacturer = device.LookupXmlKey("manufacturer"),
            ManufacturerUrl = device.LookupXmlKey("manufacturerURL"),
            ModelDescription = device.LookupXmlKey("modelDescription"),
            ModelName = device.LookupXmlKey("modelName"),
            ModelNumber = device.LookupXmlKey("modelNumber"),
            SerialNumber = device.LookupXmlKey("serialNumber"),
            Udn = device.LookupXmlKey("UDN"),
            ServiceList = SsdpServiceInfo.ParseElementServices(device)
         };
      }
   }

   public class SsdpDevice
   {
      public Uri Location { get; private set; }
      public string Server { get; private set; }
      public string ServiceType { get; private set; }
      public string UniqueServiceName { get; private set; }

      public SsdpDeviceInfo Info { get; set; }

      private SsdpDevice() { }

      public static SsdpDevice ParseBroadcastResponse(string data)
      {
         var pairs = data
            .Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Split(new[] { ": " }, 2, StringSplitOptions.None))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].ToLowerInvariant(), parts => parts[1]);

         var device = new SsdpDevice();

         string location;
         if (pairs.TryGetValue("location", out location))
            device.Location = new Uri(location);
         else
            return null;

         string server;
         if (pairs.TryGetValue("server", out server))
            device.Server = server;

         string st;
         if (pairs.TryGetValue("st", out st))
            device.ServiceType = st;

         string usn;
         if (pairs.TryGetValue("usn", out usn))
            device.UniqueServiceName = usn;

         return device;
      }
   }

}
