using System;
using System.Linq;
using System.Xml.Linq;

namespace SsdpRadar
{
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
            .LookupElement("serviceList")
            .LookupElements("service")
            .Select(e => new SsdpServiceInfo
            {
               ServiceType = e.LookupXmlKey("serviceType"),
               ServiceID = e.LookupXmlKey("serviceId"),
               ControUrl = e.LookupXmlKey("controlURL"),
               EventSubscriptionUrl = e.LookupXmlKey("eventSubURL"),
               ServiceDescriptionUrl = e.LookupXmlKey("SCPDURL")
            }).ToArray();
      }

      public override string ToString()
      {
         return $"[SsdpServiceInfo: ServiceType={ServiceType}, ServiceID={ServiceID}, ControUrl={ControUrl}, EventSubscriptionUrl={EventSubscriptionUrl}, ServiceDescriptionUrl={ServiceDescriptionUrl}]";
      }
   }
}
