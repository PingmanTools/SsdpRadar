using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SsdpRadar
{
   public delegate void DeviceFoundDelegate(SsdpDevice device);

   public interface IFinderService : IDisposable
   {
      Task<IEnumerable<SsdpDevice>> FindDevicesAsync(Action<SsdpDevice> deviceFoundCallback = null);
   }
}
