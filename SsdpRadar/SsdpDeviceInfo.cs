using System;
using System.Xml.Linq;
using System.Linq;

namespace SsdpRadar
{
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
         var device = xDocument.Root.LookupElement("device");
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

      public override string ToString()
      {
         return $"DeviceType={DeviceType}, FriendlyName={FriendlyName}, Manufacturer={Manufacturer}, ManufacturerUrl={ManufacturerUrl}, ModelDescription={ModelDescription}, ModelName={ModelName}, ModelNumber={ModelNumber}, SerialNumber={SerialNumber}, Udn={Udn}, ServiceList={string.Join("; ", ServiceList.Select(s => s.ToString()))}";
      }
   }
}