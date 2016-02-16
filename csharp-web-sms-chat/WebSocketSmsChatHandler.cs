using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Bandwidth.Net;
using Bandwidth.Net.Model;
using Microsoft.Web.WebSockets;
using Nancy.AspNet.WebSockets;
using Nancy.Json;

namespace WebSmsChat
{
  public class WebSocketSmsChatHandler : IWebSocketHandler
  {
    public void OnClose()
    {
      lock (ActiveSockets)
      {
        ActiveSockets.Remove(_client);
      }
      _client = null;
    }

    public void OnData(byte[] data)
    {
    }

    public void OnError()
    {
    }

    public void OnMessage(string json)
    {
      Debug.Print("Received new messages: {0}", json);
      var serializer = new JavaScriptSerializer();
      Dictionary<string, object> message;
      try
      {
        message = serializer.Deserialize<Dictionary<string, object>> (json);
      }
      catch (Exception ex)
      {
        Debug.Print("Invalid json format of {0}: {1}", json, ex.Message);
        return;
      }
      var command = (string)message["command"];
      Action<Exception> sendError = err => {
         _client.EmitEvent( $"{command}.error.{message["id"]}", new Dictionary<string, object>() { {"message", err.Message }});
      };
      Func<Dictionary<string, object>, WebSocketHandler, Task<object>> handler;
      if (Commands.TryGetValue(command, out handler))
      {
        handler(message, _client).ContinueWith(t =>
        {
          if (t.Exception != null)
          {
            sendError(t.Exception.InnerExceptions.First());
            return;
          }
          _client.EmitEvent($"{command}.success.{message["id"]}", t.Result);
        }).Start();
      }
      else
      {
        sendError(new Exception($"Command \"{command}\" is not implemented"));
      }
    }

    public void OnOpen(IWebSocketClient client)
    {
      _client = (WebSocketHandler)client;
      lock (ActiveSockets)
      {
        ActiveSockets.Add(_client);
      }
      Debug.Print("Connected new websocket client");
    }

    private WebSocketHandler _client;


    private static Client GetCatapultClient(Dictionary<string, object> message)
    {
      var auth = (IDictionary<string, object>) message["auth"];
      return Client.GetInstance((string)auth["userId"], (string)auth["apiToken"], (string)auth["apiSecret"]);
    }

    private static readonly Dictionary<string, Func<Dictionary<string, object>, WebSocketHandler, Task<object>>> Commands = new Dictionary<string, Func<Dictionary<string, object>, WebSocketHandler, Task<object>>>
    {
      /**
       * Check auth data, balance and return phone number for messages
       */
      { "signIn", async (message, socket) =>
        {
          message["auth"] = message["data"];
          var client = GetCatapultClient(message);
          var baseUrl = new UriBuilder(socket.WebSocketContext.RequestUri) {Path = "/"};
          var applicationName = $"web-sms-chat on ${baseUrl.Host}";
          var userId = (string)((Dictionary<string, object>) message["auth"])["userId"];
          Debug.Print("Getting application id");
          var application = (await Application.List(client, 0, 1000)).FirstOrDefault(a => a.Name == applicationName);
          if (application == null)
          {
            application = await Application.Create(new Dictionary<string, object>
            {
              {"name", applicationName},
              {"incomingMessageUrl", $"{baseUrl}/${userId}/callback" }
            });
          }
          Debug.Print("Getting phone number");
          var phoneNumber = (await PhoneNumber.List(client, new Dictionary<string, object>
          {
            {"applicationId", application.Id},
            {"size", 1}
          })).FirstOrDefault();
          if (phoneNumber == null)
          {
            Debug.Print("Reserving new phone number");
            var number = (await AvailableNumber.SearchLocal(client, new Dictionary<string, object>
            {
              {"city", "Cary"},
              {"state", "NC"},
              {"quantity", 1}
            })).First().Number;
            phoneNumber = await PhoneNumber.Create(client, new Dictionary<string, object>
            {
              {"applicationId", application.Id},
              {"number", number}
            });
          }
          socket.WebSocketContext.Items["userId"] = userId;
          return new Dictionary<string, object> { { "phoneNumber", phoneNumber.Number} };
        }
      },

      /**
       * Get messages
       */
      {"getMessages", async (message, socket) =>
        {
          var client = GetCatapultClient(message);
          var data = (Dictionary<string, object>) message["data"];
          var outMessages = await Message.List(client, new Dictionary<string, object>
          {
            {"size", 1000},
            {"direction", "out"},
            {"from", data["phoneNumber"]}
          });
          var inMessages = await Message.List(client, new Dictionary<string, object>
          {
            {"size", 1000},
            {"direction", "in"},
            {"to", data["phoneNumber"]}
          });
          var messages = inMessages.Concat(outMessages).OrderBy(m => m.Time).Select(m=>m.ToDictionary());
          var userId = (string)((Dictionary<string, object>) message["auth"])["userId"];
          socket.WebSocketContext.Items["userId"] = userId;
          return messages;
        }
      },

      /**
       * Send message
       */
      {"sendMessage", async (message, socket) =>
        {
          var client = GetCatapultClient(message);
          var data = (Dictionary<string, object>) message["data"];
          var newMessage = await Message.Create(client, data);
          var userId = (string)((Dictionary<string, object>) message["auth"])["userId"];
          socket.WebSocketContext.Items["userId"] = userId;
          return newMessage.ToDictionary();
        }
      }
    };

    public static readonly List<WebSocketHandler>  ActiveSockets = new List<WebSocketHandler>();
  }
}
