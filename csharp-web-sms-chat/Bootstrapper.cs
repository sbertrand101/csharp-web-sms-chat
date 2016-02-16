using System;
using System.Linq;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.Conventions;
using Nancy.TinyIoc;

namespace WebSmsChat
{
  public class Bootstrapper : DefaultNancyBootstrapper
  {
    protected override void ApplicationStartup(TinyIoCContainer container, IPipelines pipelines)
    {
      var staticFiles = new[] {"/smschat", "/index.html", "/config.js", "/app/", "/styles/", "/node_modules/"};
      pipelines.BeforeRequest += c =>
      {
        //SPA support
        if (c.Request.Method == "GET" &&
          !staticFiles.Any(path => c.Request.Path.IndexOf(path, StringComparison.Ordinal) >= 0))
        {
          var response = new Response {StatusCode = HttpStatusCode.MovedPermanently};
          response.Headers.Add("Location", "/index.html");
          return response;
        }
        return null;
      };
      base.ApplicationStartup(container, pipelines);
    }

    protected override void ConfigureConventions(NancyConventions conventions)
    {
      base.ConfigureConventions(conventions);

      conventions.StaticContentsConventions.Add(
          StaticContentConventionBuilder.AddDirectory("", "Content")
      );
    }
  }
}
