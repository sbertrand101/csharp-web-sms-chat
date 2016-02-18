## csharp-web-sms-chat

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


### Locally

Clone the web application.

```console
git clone https://github.com/BandwidthExamples/csharp-web-sms-chat.git
```

Open solution file in Visual Studio and build it.

You can run compiled C# code with IIS Express on local machine if you have ability to handle external requests or use any external hosting (like Azure).

Note: If you are going to use Azure as hosting please after deployment open this site in [Azure Management Console](https://manage.windowsazure.com/), select app settings and switch on `Web Sockets`. Otherwise the app will not work correctly.


