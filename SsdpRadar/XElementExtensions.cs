using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace SsdpRadar
{
   public static class XElementExtensions
   {
      public static XElement LookupElement(this XElement el, string key)
      {
         return el.Element(el.Document.Root.Name.Namespace + key);
      }

      public static IEnumerable<XElement> LookupElements(this XElement el, string key)
      {
         return el.Elements(el.Document.Root.Name.Namespace + key);
      }

      public static string LookupXmlKey(this XElement el, string key)
      {
         return el.LookupElement(key)?.Value;
      }
   }
}
