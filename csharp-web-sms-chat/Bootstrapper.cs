using System;
using System.IO;
using System.Linq;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.Conventions;
using Nancy.Responses;
using Nancy.TinyIoc;

namespace WebSmsChat
{
  public class Bootstrapper : DefaultNancyBootstrapper
  {
    protected override void ApplicationStartup(TinyIoCContainer container, IPipelines pipelines)
    {
      var staticFiles = new[]
      {"/smschat", "/index.html", "/config.js", "/vendor.js", "/app/", "/styles/", "/node_modules/", ".map"};
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

      conventions.StaticContentsConventions.Add((ctx, root) =>
      {
        string fileName;
        if (ctx.Request.Path == "/vendor.js")
        {
          //return compressed version of vendor.js (as vendor.js.gz)
          fileName = Path.GetFullPath(Path.Combine(root, "Content", "vendor.js.gz"));
          if (File.Exists(fileName))
          {
            var response = new GenericFileResponse(fileName, ctx);
            response.Headers.Add("Content-Encoding", "gzip");
            response.Headers.Add("Content-Type", "application/javascript");
            return response;
          }
        }
        fileName = Path.GetFullPath(Path.Combine(root, "Content", (ctx.Request.Path == "/")?"index.html": ctx.Request.Path.Substring(1)));
        if (File.Exists(fileName))
        {
          return new GenericFileResponse(fileName, ctx);
        }
        return null;
      });
    }
  }
}
