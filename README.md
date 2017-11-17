<div align="center">

# C Sharp Web SMS Chat

<a href="http://dev.bandwidth.com"><img src="https://s3.amazonaws.com/bwdemos/BW_Messaging.png"/></a>
</div>


<div align="center"> 
<b>This application is outdated, but will be updated soon!</b><br><br>
</div> 

C# backend for web-based chat application that features Catapult SMS and MMS capabilities

* [Creating Application](http://ap.bandwidth.com/docs/rest-api/applications/?utm_medium=social&utm_source=github&utm_campaign=dtolb&utm_content=_)
* [Searching for Phone Number](http://ap.bandwidth.com/docs/rest-api/available-numbers/#resourceGETv1availableNumberslocal/?utm_medium=social&utm_source=github&utm_campaign=dtolb&utm_content=_)
* [Ordering Phone Number](http://ap.bandwidth.com/docs/rest-api/phonenumbers/#resourcePOSTv1usersuserIdphoneNumbers/?utm_medium=social&utm_source=github&utm_campaign=dtolb&utm_content=_)

## Prerequisites
- Configured Machine with Ngrok/Port Forwarding -OR- Azure Account
  - [Ngrok](https://ngrok.com/)
  - [Azure](https://account.windowsazure.com/Home/Index)
- [Visual Studio 2015](https://www.visualstudio.com/en-us/downloads/download-visual-studio-vs.aspx)
- [Git](https://git-scm.com/)
- Common Azure Tools for Visual Studio (they are preinstalled with Visual Studio)


## Build and Deploy

### Azure One Click

[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://azuredeploy.net/)


## How it works

### Routes.cs
Routes.cs contains, well, the routes for the application.

There are two main endpoints:
* post ```/upload``` add media to Catapult
* post ```/{userId}/callback``` callback URL for catapult events

#### Grab client and upload media to catapult

```csharp
Post["/upload", true] = async (c, t) =>
{
  Debug.Print("Uploading file");
  var file = Request.Files.First();
  var fileName = Guid.NewGuid().ToString("N");
  var serializer = new JavaScriptSerializer();
  var auth = serializer.Deserialize<Dictionary<string, string>>(Request.Headers.Authorization);
  var client = Client.GetInstance(auth["UserId"], auth["ApiToken"], auth["ApiSecret"]);
  await Media.Upload(client, fileName, file.Value, file.ContentType);
  return new Dictionary<string, string>
  {
    {"fileName", fileName}
  };
};
```

#### Each user has their own callback path from catapult

```csharp
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
```

### WebSocketSmsChatHandler.cs

```WebSocketSmsChatHandler.cs``` contains all the information shuttled between the client and Server

#### Check if application already exists for user

```csharp
Debug.Print("Getting application id");
var application = (await Application.List(client, 0, 1000)).FirstOrDefault(a => a.Name == applicationName);
if (application == null)
{
  application = await Application.Create(client, new Dictionary<string, object>
  {
    {"name", applicationName},
    {"incomingMessageUrl", $"{baseUrl}{userId}/callback" }
  });
}
```

#### Check if phone number exists for user, if not create new number

```csharp
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

```

#### Messages are stored on catapult, so we need to fetch those

```csharp
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
    var userId = (string)((Dictionary<string, object>) message["Auth"])["UserId"];
    socket.WebSocketContext.Items["userId"] = userId;
    return messages;
  }
},
```

#### How to send a messages

```csharp
{"sendMessage", async (message, socket) =>
  {
    var client = GetCatapultClient(message);
    var data = (Dictionary<string, object>) message["Data"];
    var newMessage = await Message.Create(client, data);
    var userId = (string)((Dictionary<string, object>) message["Auth"])["UserId"];
    socket.WebSocketContext.Items["userId"] = userId;
    return newMessage.ToDictionary();
  }
}
```

### Locally

Clone the web application.

```console
git clone https://github.com/BandwidthExamples/csharp-web-sms-chat.git
```

Open solution file in Visual Studio and build it.

You can run compiled C# code with IIS Express on local machine if you have ability to handle external requests or use any external hosting (like Azure).

Note: If you are going to use Azure as hosting please after deployment open this site in [Azure Management Console](https://manage.windowsazure.com/), select app settings and switch on `Web Sockets`. Otherwise the app will not work correctly.
