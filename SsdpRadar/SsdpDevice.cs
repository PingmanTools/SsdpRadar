using System;
using System.Linq;
using System.Net;

namespace SsdpRadar
{
   public class SsdpDevice
   {
      public IPAddress RemoteEndPoint { get; private set; }

      public Uri Location { get; private set; }

      public string Server { get; private set; }

      public string ServiceType { get; private set; }

      public string UniqueServiceName { get; private set; }

      public SsdpDeviceInfo Info { get; set; }

      private SsdpDevice()
      {
      }

      public static SsdpDevice ParseBroadcastResponse(IPAddress endpoint, string data)
      {
         // parse http response header
         var pairs = data
            .Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Split(new[] { ":" }, 2, StringSplitOptions.None))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].ToLowerInvariant().Trim(), parts => parts[1].Trim());

         var device = new SsdpDevice();
         device.RemoteEndPoint = endpoint;

         string location;
         if (pairs.TryGetValue("location", out location))
         {
            UriBuilder uriBuilder = null;
            Uri uri = null;
            if (Uri.TryCreate(location, UriKind.Absolute, out uri))
            {
               uriBuilder = new UriBuilder(uri);
            }
            else
            {
               uriBuilder = new UriBuilder();
               uriBuilder.Host = location;
               uriBuilder.Scheme = "unknown";
            }
            device.Location = uriBuilder.Uri;
         }
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

      public override string ToString()
      {
         return ToString(full: true);
      }

      public string ToString(bool full)
      {
         if (full)
         {
            return $"Location={Location}, Server={Server}, ServiceType={ServiceType}, UniqueServiceName={UniqueServiceName}, {Info}";
         }
         else
         {
            if (Info == null)
            {
               return $"{Location}; {Server}; {ServiceType}";
            }
            else
            {
               return $"{Info.FriendlyName}; {Info.ModelName}; {Info.Manufacturer}; {Info.DeviceType}";
            }
         }
      }
   }
}
