using Java.Net;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SsdpRadar.Android
{
   public class NetworkInterfaceProvider : INetworkInterfaceProvider
   {
      public IEnumerable<byte[]> GetLocalInterfaces()
      {

         var networkInterfaces = new List<NetworkInterface>();
         var interfaceIterator = NetworkInterface.NetworkInterfaces;
         while (interfaceIterator.HasMoreElements)
         {
            var networkInterface = (NetworkInterface)interfaceIterator.NextElement();
            networkInterfaces.Add(networkInterface);
         }

         var validInterfaces = networkInterfaces
            .Where(i => !i.IsLoopback)
            .Where(i => i.IsUp);

         var interfaceAddreseses = validInterfaces
            .SelectMany(i => i.InterfaceAddresses);

         var ips = interfaceAddreseses
            .Select(i => i.Address)
            .Where(i => i is Inet4Address)
            .Where(i => i != null);

         var ipBytes = ips.Select(i => i.GetAddress());

         return ipBytes;

      }
   }
}