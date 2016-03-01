using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bandwidth.Net;
using Bandwidth.Net.Model;
using Microsoft.Web.WebSockets;
using Nancy;
using Nancy.AspNet.WebSockets;
using Nancy.Json;
using Nancy.TinyIoc;
using System.Text.RegularExpressions;

namespace WebSmsChat
{
  public class Routes : WebSocketNancyModule
  {
    public Routes(TinyIoCContainer container)
    {
      var activeUsers = WebSocketSmsChatHandler.ActiveUsers;
      WebSocket["/smschat"] = _ => new WebSocketSmsChatHandler();

      Post["/{userId}/{source}/callback", true] = async (c, t) =>
      {
        var userId = (string)c.UserId;
        var source = (string)c.Source;
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
          foreach (var socket in sockets.Where(s => (string)s.WebSocketContext.Items["userId"] == userId))
          {
            Debug.Print("Sending Catapult data to websocket client");
            socket.EmitEvent(source, data);
          }
          if (source == "call")
          {
            dynamic evnt = BaseEvent.CreateFromString(json);
            var client = WebSocketSmsChatHandler.GetCatapultClientByUserId(userId);
            if (client != null)
            {
              await ProcessCallEvent(userId, client, evnt, $"http://{Request.Url.HostName}", $"{Request.Url.HostName.Split('.').First()}.bwapp.bwsip.io");
            }

          }
          return "";
        }
      };

      Post["/upload", true] = async (c, t) =>
      {
        Debug.Print("Uploading file");
        var file = Request.Files.First();
        var fileName = $"{Guid.NewGuid().ToString("N")}-{file.Name}";
        var serializer = new JavaScriptSerializer();
        var auth = serializer.Deserialize<Dictionary<string, string>>(Request.Headers.Authorization);
        var client = Client.GetInstance(auth["UserId"], auth["ApiToken"], auth["ApiSecret"]);
        await Media.Upload(client, fileName, file.Value, file.ContentType);
        return new Dictionary<string, string>
        {
          {"fileName", fileName}
        };
      };

    }

    // for incoming calls
    private async Task ProcessCallEvent(string userId, Client client, IncomingCallEvent data, string baseUrl, string sipDomain)
    {
      var regex = new Regex("sip\\:chat\\-(\\d+)@" + sipDomain.Replace(".", "\\."));
      var toNumber = data.To;
      var fromNumber = data.From;
      Debug.Print("Current leg: {0} -> {1}", fromNumber, toNumber);
      var m = regex.Match(data.From);
      if (m != null && m.Groups.Count > 0)
      {
        //outgoing call from web ui
        fromNumber = $"+{m.Groups[1]}";
      }
      else {
        //incoming call
        fromNumber = data.To;
        toNumber = $"sip:chat-${data.To.Substring(1)}@${sipDomain}";
      }
      var currentCall = await Call.Get(client, data.CallId);
      UserInfo user;
      if (!WebSocketSmsChatHandler.ActiveUsers.TryGetValue(userId, out user))
      {
        //user closed web ui
        await currentCall.HangUp();
        return;
      }
      await currentCall.AnswerOnIncoming();
      await currentCall.PlayAudio(new Dictionary<string, object> {
        { "fileUrl", $"{baseUrl}/Content/sounds/ring.mp3" },
        { "loopEnabled", true }
      });
      var bridge = await Bridge.Create(client, new Dictionary<string, object>{
        { "callIds", new[] {data.CallId } },
        { "bridgeAudio",  true }
      });
      string bridgeId;
      if (user.ActiveCalls.TryGetValue(data.CallId, out bridgeId))
      {
        user.ActiveCalls.TryUpdate(data.CallId, bridgeId, bridge.Id);
      }
      else
      {
        user.ActiveCalls.TryAdd(data.CallId, bridge.Id);
      }
      Debug.Print("Another leg: {0} -> {1}", fromNumber, toNumber);
      var anotherCall = await Call.Create(client, new Dictionary<string, object>{
        { "from", fromNumber },
        { "to",  toNumber },
        { "bridgeId", bridge.Id },
        { "callbackUrl", $"{baseUrl}/{userId}/call/callback" },
        { "tag", data.CallId }
      });
      Debug.Print("Calls has been bridged");
      user.ActiveCalls.TryAdd(anotherCall.Id, bridge.Id);
    }

    // for hang up
    private async Task ProcessCallEvent(string userId, Client client, HangupEvent data, string baseUrl, string sipDomain)
    {
      string bridgeId;
      UserInfo user;
      if (!WebSocketSmsChatHandler.ActiveUsers.TryGetValue(userId, out user))
      {
        return;
      }
      if (!user.ActiveCalls.TryGetValue(data.CallId, out bridgeId))
      {
        return;
      }
      var bridge = await Bridge.Get(client, bridgeId);
      var calls = await bridge.GetCalls();
      foreach (var call in calls)
      {
        user.ActiveCalls.TryRemove(data.CallId, out bridgeId);
        if (call.State == "active")
        {
          Debug.Print("Hangup another call");
          await call.HangUp();
        }
      }
    }

    // for other call events
    private Task ProcessCallEvent(string userId, Client client, BaseEvent data, string baseUrl, string sipDomain)
    {
      return Task.FromResult(new object());
    }
  }
}
