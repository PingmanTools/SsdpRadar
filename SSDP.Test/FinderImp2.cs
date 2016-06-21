using System;
using System.Threading.Tasks;
using System.Net;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Net.NetworkInformation;
using System.Linq;
using System.Net.Http;
using System.Xml.Linq;
using MoreLinq;
using System.IO;

// FINDER
// Based on: https://github.com/jeremychild/NCast/blob/master/NCast/Discovery/SSDPDiscovery.cs

// Receives from unicast port ?

namespace SSDP.Test
{
   public class SSDPDiscovery
   {
      const string GenericMulticastAddress = "239.255.255.250";
      const int multicastPort = 1900;
      const int unicastPort = 1901;
      const int searchTimeOut = 1000;
      const string searchAllSsdpSearchTerm = "ssdp:all";

      public static async Task<SSDPResponse[]> Start()
      {
         var localAddresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Select(i => i.Address)
            .Distinct();

         var responses = (await Task.WhenAll(localAddresses.Select(ip => Find(ip)))).SelectMany(r => r);
         responses = await FillDeviceInfos(responses);
         return responses.ToArray();
      }

      static private Task<SSDPResponse[]> FillDeviceInfos(IEnumerable<SSDPResponse> devices)
      {
         devices = devices.GroupBy(e => e.Hash).Select(g => g.First());
         return Task.WhenAll(devices.Select(async (element) =>
               {
                  Console.WriteLine("begin query " + element.Interface);

                  var info = await GetDeviceInformation(element.Url);
                  Console.WriteLine("end query " + element.Url);
                  if (info != null)
                  {
                     element.Name = info.Name;
                     element.Information = info;
                  }
                  return element;
               }));
      }

      static private async Task<SSDPDeviceInformation> GetDeviceInformation(string url)
      {
         var information = new SSDPDeviceInformation();

         try
         {
            var client = new HttpClient();
            var response = await client.GetAsync(new Uri(url));
            if (response.StatusCode == HttpStatusCode.OK)
            {
               var xDocument = XDocument.Parse(await response.Content.ReadAsStringAsync());
               if (xDocument.Root != null)
               {
                  XNamespace rootNamespace = xDocument.Root.Name.Namespace;
                  XElement device = xDocument.Root.Element(rootNamespace + "device");
                  if (device != null)
                  {
                     XElement model = device.Element(rootNamespace + "modelName");
                     XElement manufacturer = device.Element(rootNamespace + "manufacturer");
                     XElement friendlyName = device.Element(rootNamespace + "friendlyName");
                     XElement udn = device.Element(rootNamespace + "UDN");
                     XElement deviceType = device.Element(rootNamespace + "deviceType");

                     if (model != null && !String.IsNullOrEmpty(model.Value))
                        information.Model = model.Value;
                     if (manufacturer != null && !String.IsNullOrEmpty(manufacturer.Value))
                        information.Manufacturer = manufacturer.Value;
                     if (friendlyName != null && !String.IsNullOrEmpty(friendlyName.Value))
                        information.Name = friendlyName.Value;
                     if (udn != null && !String.IsNullOrEmpty(udn.Value))
                        information.UDN = udn.Value;
                     if (deviceType != null && !String.IsNullOrEmpty(deviceType.Value))
                        information.Type = deviceType.Value;
                  }
               }

            }

         }
         catch (Exception)
         {
         }

         return information;
      }

      private static async Task<List<SSDPResponse>> Find(IPAddress address)
      {
         var list = new List<SSDPResponse>();

         try
         {
            IPEndPoint localEndPoint = new IPEndPoint(address, unicastPort);
            IPEndPoint multicastEndPoint = new IPEndPoint(IPAddress.Parse(GenericMulticastAddress), multicastPort);

            Socket udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket.ReceiveTimeout = searchTimeOut;
            udpSocket.SendTimeout = searchTimeOut;
            udpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            udpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, searchTimeOut);
            udpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, (int)ushort.MaxValue);
            udpSocket.Bind(localEndPoint);

            var ssdpAddress = GenericMulticastAddress;
            var ssdpPort = multicastPort;
            var ssdpMx = 2;
            var ssdpSt = searchAllSsdpSearchTerm;

            var ssdpRequest = "M-SEARCH * HTTP/1.1\r\n" +
                              String.Format("HOST: {0}:{1}\r\n", ssdpAddress, ssdpPort) +
                              "MAN: \"ssdp:discover\"\r\n" +
                              String.Format("MX: {0}\r\n", ssdpMx) +
                              String.Format("ST: {0}\r\n", ssdpSt) + "\r\n";
            var bytes = Encoding.UTF8.GetBytes(ssdpRequest);
            byte[] bytesReceived = new byte[(int)ushort.MaxValue];

            var endPoint = (EndPoint)localEndPoint;

            for (int index = 0; index < 3; ++index)
            {
               int totalbytes;
               Console.WriteLine("begin socket ");
               totalbytes = udpSocket.SendTo(bytes, (EndPoint)multicastEndPoint);
               while (totalbytes > 0)
               {
                  try
                  {
                     totalbytes = udpSocket.ReceiveFrom(bytesReceived, ref endPoint);
                     if (totalbytes > 0)
                     {
                        var response = Encoding.UTF8.GetString(bytesReceived, 0, totalbytes);
                        var ssdpResponse = new SSDPResponse();
                        ssdpResponse.Interface = localEndPoint.Address;
                        ssdpResponse.Parse(response);
                        list.Add(ssdpResponse);
                     }
                  }
                  catch (SocketException)
                  {
                     break;
                  }
                  catch (Exception ex)
                  {
                     break;
                  }
               }
            }

            if (udpSocket != null)
            {
               udpSocket.Close();
            }
         }
         catch (Exception)
         {
         }

         return list;
      }

   }

   public class SSDPDeviceInformation
   {
      public string Name { get; set; }

      public string Manufacturer { get; set; }

      public string Type { get; set; }

      public string Model { get; set; }

      public string UDN { get; set; }
   }

   public class SSDPResponse
   {
      public SSDPResponse()
      {
         Name = "Unknown";
      }

      public IPAddress Interface { get; set; }

      public string Response { get; set; }

      public IPAddress Address { get; set; }

      public IPEndPoint EndPoint { get; set; }

      public string USN { get; set; }

      public string Hash { get; set; }

      public string Url { get; set; }

      public string Name { get; set; }

      public SSDPDeviceInformation Information { get; set; }

      public void Parse(string response)
      {
         if (response.StartsWith("HTTP/1.1 200 OK"))
         {
            this.Response = response;

            var reader = new StringReader(response);
            var lines = new List<string>();
            for (;;)
            {
               var line = reader.ReadLine();
               if (line == null)
                  break;
               if (line != "")
                  lines.Add(line);
            }
            string location = lines.Where(lin => lin.StartsWith("LOCATION:")).FirstOrDefault();
            if (!String.IsNullOrEmpty(location))
            {
               var uri = new Uri(location.Replace("LOCATION:", ""));
               this.Url = uri.ToString();
               this.Address = IPAddress.Parse(uri.Host);
               this.EndPoint = new IPEndPoint(this.Address, uri.Port);

               var hash = String.Format("{0}:{1}", this.Address.ToString(), this.EndPoint.ToString());
               this.Hash = hash;
            }
            string usn = lines.Where(lin => lin.StartsWith("USN:")).FirstOrDefault();
            if (!String.IsNullOrEmpty(usn))
            {
               this.USN = usn.Replace("USN:", "");
            }

         }

      }

      public override string ToString()
      {
         return String.Format("{0} ({1}) @ {2}", Name, EndPoint);
      }
   }
}

