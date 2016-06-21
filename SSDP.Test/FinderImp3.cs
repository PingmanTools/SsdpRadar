using System;
using System.Collections;
using System.Collections.Generic;
using System.Timers;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text;

// FINDER
// Based on: https://gist.github.com/codebutler/418752

namespace SSDP.Test
{

   public class UPnPDiscovery
   {
      public event EventHandler DevicesChanged;

      Dictionary<string, RootDevice> devices = new Dictionary<string, RootDevice>();

      public Dictionary<string, RootDevice> RootDevices
      {
         get
         {
            return devices;
         }
      }

      IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

      Socket socket;
      UdpClient client;
      Thread unicastThread;
      Thread multicastThread;
      System.Timers.Timer searchTimer;

      public UPnPDiscovery()
      {
         searchTimer = new System.Timers.Timer(30000);
         socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
         client = new UdpClient(1900);
         client.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
      }

      public void Start()
      {
         BroadcastSearch(null, null);

         searchTimer.Elapsed += new ElapsedEventHandler(BroadcastSearch);
         searchTimer.Start();

         unicastThread = new Thread(new ThreadStart(UnicastListener));
         unicastThread.Start();

         multicastThread = new Thread(new ThreadStart(MulticastListener));
         multicastThread.Start();

      }

      public void Stop()
      {
         searchTimer.Stop();
         unicastThread.Abort();
         multicastThread.Abort();
      }

      private void BroadcastSearch(object o, System.Timers.ElapsedEventArgs args)
      {
         StringBuilder builder = new StringBuilder();
         builder.Append("M-SEARCH * HTTP/1.1\r\n");
         builder.Append("Host:239.255.255.250:1900\r\n");
         builder.Append("Man:\"ssdp:discover\"\r\n");
         //builder.Append ("ST:upnp:rootdevice\r\n");
         builder.Append("ST:ssdp:all\r\n");
         builder.Append("MX:3\r\n");
         builder.Append("\r\n");
         builder.Append("\r\n");

         byte[] data = Encoding.ASCII.GetBytes(builder.ToString());

         lock (socket)
            socket.SendTo(data, remoteEndPoint);
      }

      private void MulticastListener()
      {
         while (true)
         {
            IPEndPoint ep = null;
            byte[] data = client.Receive(ref ep);
            //Console.WriteLine ("Multicast Received: " + data.Length);
            string message = Encoding.ASCII.GetString(data, 0, data.Length);
            ProcessMessage(message);
         }
      }

      private void UnicastListener()
      {
         while (true)
         {
            //Console.WriteLine ("Receiving...");
            byte[] data = new byte[8192];
            EndPoint ep = (EndPoint)remoteEndPoint;

            int bytesReceived = 0;
            lock (socket)
               bytesReceived = socket.ReceiveFrom(data, ref ep);

            //Console.WriteLine ("Received: " + data.Length);

            string message = Encoding.ASCII.GetString(data, 0, bytesReceived);
            ProcessMessage(message);
         }
      }

      private void ProcessMessage(string message)
      {
         Dictionary<string, string> headers = new Dictionary<string, string>();

         // Fuck this
         message = message.Replace("\r", "");

         string[] lines = message.Split(new char[] { '\n' });

         if (lines[0].StartsWith("HTTP/") | lines[0].StartsWith("NOTIFY "))
         {
            for (int x = 1; x < lines.Length; x++)
            {
               string line = lines[x];
               if (line.Trim() != "")
               {
                  string key = line.Substring(0, line.IndexOf(":")).Trim().ToLower();
                  string val = line.Substring(line.IndexOf(":") + 1).Trim();
                  //Console.WriteLine (key + "==" + val);
                  headers.Add(key, val);
               }
            }
         }
         else if (lines[0].StartsWith("NOTIFY "))
         {
            //TODO: Do anything here?
            Console.WriteLine("GOT NOTIFICATION !!!");
            Console.WriteLine(message);
            return;
         }
         else if (lines[0].StartsWith("M-SEARCH "))
         {
            // do anything here?
            return;
         }
         else
         {
            throw new Exception("WTF!?!?!?! " + lines[0]);
         }

         string usn = headers["usn"].ToString();
         string[] usnItems = usn.Split(':');

         string uuid = usnItems[1];

         Console.WriteLine("USN ITEMS: " + usnItems.Length);

         if (usnItems.Length == 5)
         {
            if (headers["nts"] == "ssdp:alive")
            {
               if (usnItems[usnItems.Length - 1] == "rootdevice")
               {
                  RootDevice device = new RootDevice(headers["location"], uuid);
                  if (devices.ContainsKey(device.Location))
                     devices.Remove(device.Location);
                  devices.Add(device.Location, device);

                  if (DevicesChanged != null)
                     DevicesChanged(this, null);
               }
               else
               {
                  throw new Exception("WTF IS THIS THING?!");
               }
            }
            else if (headers["nts"] == "ssdp:byebye")
            {
               Console.WriteLine("DELETE SOMETHING!!!");
            }
            else
            {
               throw new Exception("WAT !! " + headers["nts"]);
            }
         }
         else if (usnItems.Length == 8)
         {
            if (usnItems[usnItems.Length - 3] == "service")
            {
               string serviceType = usnItems[usnItems.Length - 2];

               Device device = devices[headers["location"]];

               if ((device as RootDevice).Devices.ContainsKey(uuid) == true)
                  device = (device as RootDevice).Devices[uuid];

               device.Services.Add(new Service(uuid, serviceType));

               if (DevicesChanged != null)
                  DevicesChanged(this, null);

            }
            else if (usnItems[usnItems.Length - 3] == "device")
            {
               string deviceType = usnItems[usnItems.Length - 2];

               RootDevice device = devices[headers["location"]];

               if (device.UUID == uuid)
               {
                  device.Type = deviceType;
               }
               else
               {
                  if (device.Devices.ContainsKey(uuid) == false)
                     device.Devices.Add(uuid, new Device(uuid, deviceType));
                  else
                     device.Devices[uuid].Type = deviceType;
               }

               if (DevicesChanged != null)
                  DevicesChanged(this, null);

            }
         }
         else if (usnItems.Length == 2)
         {
            RootDevice rootDevice = devices[headers["location"]];
            if (rootDevice.UUID != uuid)
            {
               if (rootDevice.Devices.ContainsKey(uuid) == false)
               {
                  rootDevice.Devices.Add(uuid, new Device(uuid, "UNKNOWN"));
               }
            }

         }
         else
         {
            throw new Exception("Unknown length: " + usnItems.Length + "  " + usn);
         }
      }
   }

   public class RootDevice : Device
   {
      string location;

      public Dictionary<string, Device> Devices = new Dictionary<string, Device>();


      public string Location
      {
         get
         {
            return location;
         }
      }

      public RootDevice(string location, string uuid) : base(uuid, "rootdevice")
      {
         this.location = location;
      }

      public string ToString()
      {
         if (Type == "")
            return location;
         else
            return Type;
      }

   }

   public class Device
   {
      public List<Service> Services = new List<Service>();

      string uuid;
      string deviceType;

      public string Type
      {
         get
         {
            return deviceType;
         }
         set
         {
            deviceType = value;
         }
      }

      public string UUID
      {
         get
         {
            return uuid;
         }
      }

      public Device(string uuid, string deviceType)
      {
         this.uuid = uuid;
         this.deviceType = deviceType;
      }
   }

   public class Service
   {
      string uuid;
      string type;

      public string Type
      {
         get
         {
            return type;
         }
      }

      public string UUID
      {
         get
         {
            return uuid;
         }
      }

      public Service(string uuid, string type)
      {
         this.uuid = uuid;
         this.type = type;
      }
   }
}