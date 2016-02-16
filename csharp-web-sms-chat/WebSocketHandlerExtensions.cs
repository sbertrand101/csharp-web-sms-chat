using System.Collections.Generic;
using Microsoft.Web.WebSockets;
using Nancy.Json;

namespace WebSmsChat
{
  public static  class WebSocketHandlerExtensions
  {
    public static void EmitEvent(this WebSocketHandler socket, string eventName, object data = null)
    {
      if (socket == null) return;
      var serializer = new JavaScriptSerializer();
      socket.Send(serializer.Serialize(new Dictionary<string, object> { { "eventName", eventName }, { "data", data } }));
    }
  }
}
