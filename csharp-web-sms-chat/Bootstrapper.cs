using System;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.TinyIoc;

namespace WebSmsChat
{
  public class Bootstrapper : DefaultNancyBootstrapper
  {
    protected override void ApplicationStartup(TinyIoCContainer container, IPipelines pipelines)
    {
      pipelines.BeforeRequest += c =>
      {
        //SPA support
        if (c.Request.Method == "GET" && c.Request.Path.IndexOf("/Content/", StringComparison.Ordinal) < 0)
        {
          var response = new Response {StatusCode = HttpStatusCode.MovedPermanently};
          response.Headers.Add("Location", "/Content/index.html");
          return response;
        }
        return null;
      };
      base.ApplicationStartup(container, pipelines);
    }
  }
}
