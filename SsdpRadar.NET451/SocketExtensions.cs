using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SsdpRadar
{
   class SocketReceiveFromResult
   {
      public int ReceivedBytes;
      public EndPoint RemoteEndPoint;
   }

   static class SocketExtensions
   {
      public static async Task<SocketReceiveFromResult> ReceiveFromAsync(this Socket socket, ArraySegment<byte> buffer, SocketFlags socketFlags, EndPoint endpoint)
      {
         try
         {
            var tcs = new TaskCompletionSource<IAsyncResult>();
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.None, 0);
            socket.BeginReceiveFrom(buffer.Array, buffer.Offset, buffer.Count, SocketFlags.None, ref endpoint, r => tcs.SetResult(r), null);
            var receiveResult = socket.EndReceiveFrom(await tcs.Task, ref remoteEndPoint);
            return new SocketReceiveFromResult { ReceivedBytes = receiveResult, RemoteEndPoint = remoteEndPoint };
         }
         catch (ObjectDisposedException)
         {
            return new SocketReceiveFromResult();
         }
      }

      public static async Task<int> SendToAsync(this Socket socket, ArraySegment<byte> buffer, SocketFlags socketFlags, EndPoint endpoint)
      {
         try
         {
            var sendCompletion = new TaskCompletionSource<IAsyncResult>();
            socket.BeginSendTo(buffer.Array, buffer.Offset, buffer.Count, SocketFlags.None, endpoint, r => sendCompletion.SetResult(r), null);
            return socket.EndSendTo(await sendCompletion.Task);
         }
         catch (ObjectDisposedException)
         {
            return 0;
         }
      }
   }
}
