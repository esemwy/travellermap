// Compile-time shim providing minimal System.Web stubs so existing server code
// continues to compile against net8.0. All members throw NotImplementedException
// at runtime. Replaced by ASP.NET Core equivalents in issue #3.
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Web.Caching;
using System.Web.Routing;

namespace System.Web
{
    public abstract class HttpContextBase
    {
        public virtual HttpRequestBase Request => throw new NotImplementedException();
        public virtual HttpResponseBase Response => throw new NotImplementedException();
        public virtual Collections.IDictionary Items => throw new NotImplementedException();
        public virtual Caching.Cache Cache => throw new NotImplementedException();
        public virtual HttpServerUtility Server => throw new NotImplementedException();
    }

    public abstract class HttpRequestBase
    {
        public virtual string Path => throw new NotImplementedException();
        public virtual Uri Url => throw new NotImplementedException();
        public virtual bool IsLocal => throw new NotImplementedException();
        public virtual NameValueCollection QueryString => throw new NotImplementedException();
        public virtual NameValueCollection Form => throw new NotImplementedException();
        public virtual NameValueCollection Headers => throw new NotImplementedException();
        public virtual string[] AcceptTypes => throw new NotImplementedException();
        public virtual string ContentType => throw new NotImplementedException();
        public virtual string? UserAgent => throw new NotImplementedException();
        public virtual HttpFileCollectionBase Files => throw new NotImplementedException();
        public virtual string? this[string key] => throw new NotImplementedException();
    }

    public abstract class HttpResponseBase
    {
        public virtual int StatusCode { get; set; }
        public virtual string StatusDescription { get; set; } = "";
        public virtual string? ContentType { get; set; }
        public virtual Encoding ContentEncoding { get; set; } = Encoding.UTF8;
        public virtual bool TrySkipIisCustomErrors { get; set; }
        public virtual TextWriter Output => throw new NotImplementedException();
        public virtual Stream OutputStream => throw new NotImplementedException();
        public virtual HttpCachePolicy Cache => throw new NotImplementedException();
        public virtual void AddHeader(string name, string value) => throw new NotImplementedException();
        public virtual void AppendHeader(string name, string value) => throw new NotImplementedException();
        public virtual void Flush() => throw new NotImplementedException();
        public virtual void Close() => throw new NotImplementedException();
        public virtual void End() => throw new NotImplementedException();
        public virtual void Clear() => throw new NotImplementedException();
        public virtual void Write(string s) => throw new NotImplementedException();
        public virtual void TransmitFile(string filename) => throw new NotImplementedException();
        public virtual void Redirect(string url, bool endResponse = true) => throw new NotImplementedException();
        public virtual void SetCookie(HttpCookie cookie) => throw new NotImplementedException();
    }

    public abstract class HttpFileCollectionBase
    {
        public virtual HttpPostedFileBase? this[string name] => throw new NotImplementedException();
    }

    public abstract class HttpPostedFileBase
    {
        public virtual Stream InputStream => throw new NotImplementedException();
        public virtual string ContentType => throw new NotImplementedException();
        public virtual string FileName => throw new NotImplementedException();
        public virtual int ContentLength => throw new NotImplementedException();
    }

    public class HttpContext : HttpContextBase
    {
        public static HttpContext Current => throw new NotImplementedException();
        public new HttpRequest Request => throw new NotImplementedException();
        public new HttpResponse Response => throw new NotImplementedException();
        public override Collections.IDictionary Items => throw new NotImplementedException();
        public override Caching.Cache Cache => throw new NotImplementedException();
        public override HttpServerUtility Server => throw new NotImplementedException();
    }

    public class HttpRequest : HttpRequestBase
    {
        public override string Path => throw new NotImplementedException();
        public override Uri Url => throw new NotImplementedException();
        public override bool IsLocal => throw new NotImplementedException();
        public override NameValueCollection QueryString => throw new NotImplementedException();
        public override NameValueCollection Form => throw new NotImplementedException();
        public override NameValueCollection Headers => throw new NotImplementedException();
        public override string[] AcceptTypes => throw new NotImplementedException();
        public override string ContentType => throw new NotImplementedException();
        public override string? UserAgent => throw new NotImplementedException();
        public override HttpFileCollectionBase Files => throw new NotImplementedException();
        public override string? this[string key] => throw new NotImplementedException();
    }

    public class HttpResponse : HttpResponseBase
    {
        public override int StatusCode { get; set; }
        public override string StatusDescription { get; set; } = "";
        public override string? ContentType { get; set; }
        public override Encoding ContentEncoding { get; set; } = Encoding.UTF8;
        public override bool TrySkipIisCustomErrors { get; set; }
        public override TextWriter Output => throw new NotImplementedException();
        public override Stream OutputStream => throw new NotImplementedException();
        public override HttpCachePolicy Cache => throw new NotImplementedException();
        public override void AddHeader(string name, string value) => throw new NotImplementedException();
        public override void AppendHeader(string name, string value) => throw new NotImplementedException();
        public override void Flush() => throw new NotImplementedException();
        public override void Close() => throw new NotImplementedException();
        public override void End() => throw new NotImplementedException();
        public override void Clear() => throw new NotImplementedException();
        public override void Write(string s) => throw new NotImplementedException();
        public override void TransmitFile(string filename) => throw new NotImplementedException();
        public override void Redirect(string url, bool endResponse = true) => throw new NotImplementedException();
        public override void SetCookie(HttpCookie cookie) => throw new NotImplementedException();
    }

    public class HttpPostedFile : HttpPostedFileBase
    {
        public override Stream InputStream => throw new NotImplementedException();
        public override string ContentType => throw new NotImplementedException();
        public override string FileName => throw new NotImplementedException();
        public override int ContentLength => throw new NotImplementedException();
    }

    public class HttpFileCollection : HttpFileCollectionBase
    {
        public override HttpPostedFileBase? this[string name] => throw new NotImplementedException();
    }

    public class HttpCookie
    {
        public HttpCookie(string name) { Name = name; }
        public string Name { get; }
        public string Value { get; set; } = "";
        public bool HttpOnly { get; set; }
    }

    public class HttpCachePolicy
    {
        public void SetCacheability(HttpCacheability cacheability) { }
        public void SetMaxAge(TimeSpan maxAge) { }
        public void SetExpires(DateTime date) { }
        public void SetLastModified(DateTime date) { }
        public void SetETag(string etag) { }
        public void SetOmitVaryStar(bool omit) { }
        public void SetValidUntilExpires(bool validUntilExpires) { }
        public HttpCacheVaryByHeaders VaryByHeaders { get; } = new HttpCacheVaryByHeaders();
        public HttpCacheVaryByParams VaryByParams { get; } = new HttpCacheVaryByParams();
    }

    public class HttpCacheVaryByHeaders
    {
        public bool this[string header] { get => false; set { } }
    }

    public class HttpCacheVaryByParams
    {
        public bool this[string param] { get => false; set { } }
    }

    public enum HttpCacheability { NoCache, Private, Public, ServerAndPrivate, ServerAndNoCache, Server }

    public class HttpApplication
    {
        public HttpContext Context => throw new NotImplementedException();
        public HttpRequest Request => throw new NotImplementedException();
        public HttpResponse Response => throw new NotImplementedException();
        public event EventHandler? EndRequest;
        public event EventHandler? BeginRequest;
        protected void RaiseEndRequest() => EndRequest?.Invoke(this, EventArgs.Empty);
    }

    public class HttpRuntime
    {
        public static Caching.Cache Cache => throw new NotImplementedException();
    }

    public class HttpServerUtility
    {
        public string MapPath(string path) => throw new NotImplementedException();
    }

    public interface IHttpHandler
    {
        bool IsReusable { get; }
        void ProcessRequest(HttpContext context);
    }

    public interface IHttpModule
    {
        string ModuleName { get; }
        void Init(HttpApplication application);
        void Dispose();
    }
}

namespace System.Web.Routing
{
    public abstract class RouteBase
    {
        public abstract RouteData? GetRouteData(HttpContextBase httpContext);
        public abstract VirtualPathData? GetVirtualPath(RequestContext requestContext, RouteValueDictionary values);
    }

    public class Route : RouteBase
    {
        public RouteValueDictionary? Defaults { get; set; }
        public IRouteHandler? RouteHandler { get; set; }

        public Route(string? url, RouteValueDictionary? defaults, IRouteHandler? routeHandler)
        {
            Defaults = defaults;
            RouteHandler = routeHandler;
        }

        public override RouteData? GetRouteData(HttpContextBase httpContext) => throw new NotImplementedException();
        public override VirtualPathData? GetVirtualPath(RequestContext requestContext, RouteValueDictionary values) => throw new NotImplementedException();
    }

    public class RouteData
    {
        public RouteValueDictionary Values { get; } = new RouteValueDictionary();
        public RouteBase? Route { get; }
        public IRouteHandler? RouteHandler { get; }

        public RouteData() { }
        public RouteData(RouteBase route, IRouteHandler? routeHandler)
        {
            Route = route;
            RouteHandler = routeHandler;
        }
    }

    public class RouteValueDictionary : Dictionary<string, object?>
    {
        public RouteValueDictionary() { }
        public RouteValueDictionary(object? values) { }
        public RouteValueDictionary(IDictionary<string, object?> values) : base(values) { }
    }

    public class RouteCollection : List<RouteBase>
    {
        public new void Add(RouteBase route) => base.Add(route);
    }

    public static class RouteTable
    {
        public static RouteCollection Routes { get; } = new RouteCollection();
    }

    public interface IRouteHandler
    {
        IHttpHandler? GetHttpHandler(RequestContext requestContext);
    }

    public class RequestContext
    {
        public HttpContextBase HttpContext { get; }
        public RouteData RouteData { get; }

        public RequestContext(HttpContextBase httpContext, RouteData routeData)
        {
            HttpContext = httpContext;
            RouteData = routeData;
        }
    }

    public class VirtualPathData
    {
        public string? VirtualPath { get; set; }
    }
}

namespace System.Web.Hosting
{
    public static class HostingEnvironment
    {
        public static string? ApplicationPhysicalPath => throw new NotImplementedException();
        public static string MapPath(string virtualPath) => throw new NotImplementedException();
    }
}

namespace System.Web.Caching
{
    public class Cache
    {
        public object? this[string key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public void Insert(string key, object value) => throw new NotImplementedException();
        public void Insert(string key, object value, System.Web.Caching.CacheDependency? dependencies,
            DateTime absoluteExpiration, TimeSpan slidingExpiration,
            CacheItemPriority priority, CacheItemRemovedCallback? onRemoveCallback) => throw new NotImplementedException();
        public object? Remove(string key) => throw new NotImplementedException();
    }

    public class CacheDependency { }

    public delegate void CacheItemRemovedCallback(string key, object value, CacheItemRemovedReason reason);

    public enum CacheItemRemovedReason { Removed, Expired, Underused, DependencyChanged }

    public enum CacheItemPriority { Default, Low, BelowNormal, Normal, AboveNormal, High, NotRemovable }
}
