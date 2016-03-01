using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Bandwidth.Net;
using Bandwidth.Net.Model;
using Microsoft.Web.WebSockets;
using Nancy.AspNet.WebSockets;
using Nancy.Json;
using System.Collections.Concurrent;
using System.Threading;

namespace WebSmsChat
{
  public class WebSocketSmsChatHandler : IWebSocketHandler
  {
    public void OnClose()
    {
      UserInfo user;
      var userId = (string)_client.WebSocketContext.Items["userId"];
      if (ActiveUsers.TryGetValue(userId, out user))
      {
        Interlocked.Decrement(ref user.Counter);
        if (user.Counter == 0)
        {
          Debug.Print("User {0} has no active connections", userId);
          HangUpCalls(userId, user).ContinueWith(r => Trace.WriteLine(r.Exception.ToString()), TaskContinuationOptions.OnlyOnFaulted);
          ActiveUsers.TryRemove(userId, out user);
        }
      }
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
        message = serializer.Deserialize<Dictionary<string, object>>(json);
      }
      catch (Exception ex)
      {
        Debug.Print("Invalid json format of {0}: {1}", json, ex.Message);
        return;
      }
      var command = (string)message["Command"];
      var messageId = Convert.ToString(message["Id"], CultureInfo.InvariantCulture);
      Action<Exception> sendError = err =>
      {
        _client.EmitEvent($"{command}.error.{messageId}", new Dictionary<string, object>() { { "message", err.Message } });
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
          _client.EmitEvent($"{command}.success.{messageId}", t.Result);
        });
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
      var auth = (IDictionary<string, object>)message["Auth"];
      return Client.GetInstance((string)auth["UserId"], (string)auth["ApiToken"], (string)auth["ApiSecret"]);
    }

    public static Client GetCatapultClientByUserId(string userId)
    {
      UserInfo user;
      if (ActiveUsers.TryGetValue(userId, out user))
      {
        return Client.GetInstance(userId, user.ApiToken, user.ApiSecret);
      }
      return null;
    }

    private async Task HangUpCalls(string userId, UserInfo user)
    {
      var client = GetCatapultClientByUserId(userId);
      if (client == null)
      {
        return;
      }
      var calls = await Task.WhenAll(user.ActiveCalls.Keys.Select(callId => Call.Get(client, callId)));
      foreach (var call in calls.Where(c => c.State == "active"))
      {
        await call.HangUp(); // hang up active calls
      }
    }

    private static void SetUserData(WebSocketHandler socket, Dictionary<string, object> message)
    {
      if (socket.WebSocketContext.Items["userId"] != null)
      {
        return;
      }
      var auth = (Dictionary<string, string>)message["Auth"];
      var userId = auth["UserId"];
      socket.WebSocketContext.Items["userId"] = userId;
      UserInfo user;
      if (ActiveUsers.TryGetValue(userId, out user))
      {
        Interlocked.Increment(ref user.Counter);
      }
      else
      {
        user = new UserInfo
        {
          ApiToken = auth["ApiToken"],
          ApiSecret = auth["ApiSecret"],
          Counter = 1
        };
        ActiveUsers.TryAdd(userId, user);
      }
    }

    private static readonly Dictionary<string, Func<Dictionary<string, object>, WebSocketHandler, Task<object>>> Commands = new Dictionary<string, Func<Dictionary<string, object>, WebSocketHandler, Task<object>>>
    {
      /**
       * Check auth data, balance and return phone number for messages
       */
      { "signIn", async (message, socket) =>
        {
          message["Auth"] = message["Data"];
          var client = GetCatapultClient(message);
          var baseUrl = new UriBuilder(socket.WebSocketContext.RequestUri) {Path = "/"};
          var applicationName = $"web-sms-chat on {baseUrl.Host}";
          var userId = (string)((Dictionary<string, object>) message["Auth"])["UserId"];
          var domainName = socket.WebSocketContext.RequestUri.Host.Split('.').First();
          Debug.Print("Getting application id");
          var application = await GetApplication(client,baseUrl,applicationName,userId);
          Debug.Print("Getting phone number");
          var phoneNumber = await GetPhoneNumber(client,application);
          var userName = $"chat-{phoneNumber.Number.Substring(1)}";
          Debug.Print("Getting domain");
          var domain = await GetDomain(client, domainName);
          var password = domain.Id.Substring(3, 20);
          var sipDomain = $"{domainName}.bwapp.bwsip.io";
          Debug.Print("Getting endpoint");
          var endpoint = await GetEndpoint(domain, phoneNumber.Number, userName, application.Id, password);
          Debug.Print("Creating auth token");
          var auth = await endpoint.CreateAuthToken();
          SetUserData(socket, message);
          return new Dictionary<string, object> {
            { "phoneNumber", phoneNumber.Number},
            { "userName", userName},
            { "authToken", auth.Token},
            { "domain", sipDomain}
          };
        }
      },

      /**
       * Get messages
       */
      {"getMessages", async (message, socket) =>
        {
          var client = GetCatapultClient(message);
          var data = (Dictionary<string, object>) message["Data"];
          var outMessages = await Message.List(client, new Dictionary<string, object>
          {
            {"size", 1000},
            {"direction", "out"},
            {"from", data["PhoneNumber"]}
          });
          var inMessages = await Message.List(client, new Dictionary<string, object>
          {
            {"size", 1000},
            {"direction", "in"},
            {"to", data["PhoneNumber"]}
          });
          var messages = inMessages.Concat(outMessages).OrderBy(m => m.Time).Select(m=>m.ToDictionary());
          SetUserData(socket, message);
          return messages;
        }
      },

      /**
       * Send message
       */
      {"sendMessage", async (message, socket) =>
        {
          var client = GetCatapultClient(message);
          var data = (Dictionary<string, object>) message["Data"];
          var newMessage = await Message.Create(client, data);
          SetUserData(socket, message);
          return newMessage.ToDictionary();
        }
      },

      /**
      * Reconnect
      */
      {"reconnect", (message, socket) =>
        {
          SetUserData(socket, message);
          return Task.FromResult(new object());
        }
      }

    };

    private static async Task<PhoneNumber> GetPhoneNumber(Client client, Application application)
    {
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

      return phoneNumber;
    }

    private static async Task<Application> GetApplication(Client client, UriBuilder baseUrl, string applicationName, string userId)
    {
      var application = (await Application.List(client, 0, 1000)).FirstOrDefault(a => a.Name == applicationName);
      if (application == null)
      {
        application = await Application.Create(client, new Dictionary<string, object>
            {
              {"name", applicationName},
              {"incomingMessageUrl", $"{baseUrl}{userId}/message/callback" },
              {"incomingCallUrl", $"{baseUrl}{userId}/call/callback" },
              {"autoAnswer", false }
            });
      }

      return application;
    }

    private static async Task<Domain> GetDomain(Client client, string domainName)
    {
      return (await Domain.List(client)).FirstOrDefault(d => d.Name == domainName)
        ?? (await Domain.Create(client, new Dictionary<string, object> { { "name", domainName } }));
    }

    private static async Task<EndPoint> GetEndpoint(Domain domain, string phoneNumber, string userName, string applicationId, string password)
    {
      return (await domain.GetEndPoints()).FirstOrDefault(d => d.Name == userName)
        ?? (await domain.CreateEndPoint(new Dictionary<string, object> {
          { "name", userName },
          { "description", $"WebSms sip account for number {phoneNumber}" },
          { "domainId", domain.Id },
          { "applicationId", applicationId },
          { "enabled", true },
          { "credentials", new Dictionary<string, string> { { "password", password } } }
        }));
    }

    public static readonly List<WebSocketHandler> ActiveSockets = new List<WebSocketHandler>();
    public static readonly ConcurrentDictionary<string, UserInfo> ActiveUsers = new ConcurrentDictionary<string, UserInfo>();
  }
}
