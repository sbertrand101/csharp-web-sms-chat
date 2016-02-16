using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Bandwidth.Net;
using Bandwidth.Net.Model;
using Microsoft.Web.WebSockets;
using Nancy.AspNet.WebSockets;
using Nancy.Json;

namespace WebSmsChat
{
  public class Routes : WebSocketNancyModule
  {
    public Routes()
    {
      WebSocket["/smschat"] = _ => new WebSocketSmsChatHandler();

      Post["/{userId}/callback", true] = async (c, t) =>
      {
        var userId = (string)c.UserId;
        Debug.Print("Handling Catapult callback for user Id {0}", userId);
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
          var json = await reader.ReadToEndAsync();
          Debug.Print("Data from Catapult for {0}: {1}", userId, json);
          var serializer = new JavaScriptSerializer();
          var data = serializer.DeserializeObject(json);
          WebSocketHandler[] sockets;
          lock (WebSocketSmsChatHandler.ActiveSockets)
          {
            sockets = WebSocketSmsChatHandler.ActiveSockets.ToArray();
          }
          foreach (var socket in sockets.Where(s => (string) s.WebSocketContext.Items["userId"] == userId))
          {
            Debug.Print("Sending Catapult data to websocket client");
            socket.EmitEvent("message", data);
          }
          return "";
        }
      };

      Post["/upload", true] = async (c, t) =>
      {
        Debug.Print("Uploading file");
        var file = Request.Files.First();
        var fileName = $"{Guid.NewGuid().ToString("B")}-{file.Name}";
        var serializer = new JavaScriptSerializer();
        var auth = serializer.Deserialize<Dictionary<string, string>>(Request.Headers.Authorization);
        var client = Client.GetInstance(auth["userId"], auth["apiToken"], auth["apiSecret"]);
        await Media.Upload(client, fileName, file.Value, file.ContentType);
        return new Dictionary<string, string>
        {
          {"fileName", fileName}
        };
      };
    }
  }
}
