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

namespace SSDP.Test
{
   class MainClass
   {
      public static void Main(string[] args)
      {
         Task.Run(async () =>
            {
               var f = await SSDPDiscovery.Start();
               Console.WriteLine("Hello World!");
            });

         /*Task.Run(() =>
         {
            var ssdpFinder = new SsdpFinder();
            var xmls = "";
            ssdpFinder.DeviceFound += device =>
            {
               lock (xmls)
               {
                  xmls += device.Info.RawXml;
               }
            };
            ssdpFinder.Start();
         });*/

         System.Threading.Thread.Sleep(TimeSpan.FromHours(1));

      }


   }



}
