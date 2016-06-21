using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// FINDER
// Based on: https://github.com/gavdraper/SSDPSonos/blob/master/SSDPSharp/SsdpLocator.cs

// receives from multicast port?

namespace SSDP.Test
{
   public class SsdpDevicex
   {
      public string Location { get; set; }

      public string ModelName { get; set; }

      public string Server { get; set; }
   }

   public class SsdpLocator
   {
      public const string GenericMulticastAddress = "239.255.255.250";
      readonly IPAddress multicastAddress = IPAddress.Parse(GenericMulticastAddress);
      const int multicastPort = 1900;
      const int unicastPort = 1901;
      const int searchTimeOut = 1000;
      const string searchAllSsdpSearchTerm = "ssdp:all";

      const string messageHeader = "M-SEARCH * HTTP/1.1";
      const string messageHost = "HOST: 239.255.255.250:1900";
      const string messageMan = "MAN: \"ssdp:discover\"";
      const string messageMx = "MX: 3";
      //const string messageSt = "ST: urn:schemas-upnp-org:device:ZonePlayer:1";
      const string messageSt = "ST: " + searchAllSsdpSearchTerm;

      readonly byte[] broadcastMessage = Encoding.UTF8.GetBytes(
                                            string.Format("{1}{0}{2}{0}{3}{0}{4}{0}{5}{0}{0}",
                                               "\r\n",
                                               messageHeader,
                                               messageHost,
                                               messageMan,
                                               messageMx,
                                               messageSt));

      public ObservableCollection<SsdpDevicex> Devices { get; set; }

      public SsdpLocator()
      {
         Devices = new ObservableCollection<SsdpDevicex>();
      }

      public void CreateSsdpListener()
      {
         using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
         {
            socket.Bind(new IPEndPoint(IPAddress.Any, unicastPort));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(multicastAddress, IPAddress.Any));
            var thd = new Thread(() => GetSocketResponse(socket));
            socket.SendTo(broadcastMessage, 0, broadcastMessage.Length, SocketFlags.None, new IPEndPoint(multicastAddress, multicastPort));
            thd.Start();
            Thread.Sleep(searchTimeOut);
            socket.Close();
         }
      }

      public string GetLocation(string str)
      {
         if (str.StartsWith("HTTP/1.1 200 OK"))
         {
            var reader = new StringReader(str);
            var lines = new List<string>();
            for (;;)
            {
               var line = reader.ReadLine();
               if (line == null)
                  break;
               if (line != "")
                  lines.Add(line);
            }
            var location = lines.Where(lin => lin.ToLower().StartsWith("location:")).First();
            if (!string.IsNullOrEmpty(location) &&
                (
                   Devices.Count == 0 ||
                   (from d in Devices
                    where d.Location == location
                    select d).FirstOrDefault() == null))
            {
               return location.Replace("LOCATION: ", "");
            }
         }
         return "";
      }

      public void GetSocketResponse(Socket socket)
      {
         try
         {
            while (true)
            {
               var response = new byte[8000];
               EndPoint ep = new IPEndPoint(IPAddress.Any, multicastPort);
               socket.ReceiveFrom(response, ref ep);
               var str = Encoding.UTF8.GetString(response);
               var location = GetLocation(str);
               if (!string.IsNullOrEmpty(location))
               {
                  Devices.Add(new SsdpDevicex() { Location = location });
               }
            }
         }
         catch (Exception ex)
         {
            //TODO handle exception for when connection closes
         }

      }



   }
}
