using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace SsdpRadar.Console
{
   class Program
   {
      static void Main(string[] args)
      {
         Task.Run(() => Run());
         System.Console.ReadLine();
         Thread.Sleep(10000000);
      }

      static void Run()
      {
         var networkInterfaceProvider = new NetworkInterfaceProvider();
         var service = new SsdpRadar.FinderService(networkInterfaceProvider);
         service.DeviceFound += Service_DeviceFound;
         service.Start();
      }

      private static void Service_DeviceFound(SsdpDevice device)
      {
         if (device.Info != null)
         {
            /*System.Console.WriteLine(Environment.NewLine + string.Join(Environment.NewLine, (new string[][]
                  {
                     new []{ "Location", device.Location.ToString() },
                     new []{ "Name", device.Info.FriendlyName },
                     new [] { "Type", device.Info.DeviceType },
                     new [] { "Manufacturer", device.Info.Manufacturer },
                     new [] { "Model", device.Info.ModelName }
                  }).Select(p => p[0] + ": " + p[1])));*/
            //System.Console.WriteLine(device.ToString());
            System.Console.WriteLine(device.Info.FriendlyName);
         }
         else
         {
            System.Console.WriteLine(device.Server);
         }
      }
   }
}
