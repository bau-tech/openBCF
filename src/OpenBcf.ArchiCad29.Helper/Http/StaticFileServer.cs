using System.IO;
using System.Net;

namespace OpenBcf.ArchiCad29.Helper.Http;

/// <summary>
/// Serves the DUI3 frontend's compiled output (wwwroot/, copied next to this exe - see the .csproj)
/// over plain HTTP on 127.0.0.1. Required because DG::Browser (ArchiCAD's native embedded browser -
/// see ../../OpenBcf.ArchiCad29.NativeAddOn/Src/BcfPalette.cpp) cannot load the frontend's
/// ES-module-based bundle (&lt;script type="module"&gt;) from a file:// URL - Chromium blocks
/// module script fetches from file: origins - the same reason a real, independently-verified
/// ArchiCAD add-on (github.com/byggstyrning/ifctester-revit) runs its own local HTTP server for
/// exactly this purpose rather than using LoadHTML/file://.
/// </summary>
internal sealed class StaticFileServer
{
    private static readonly Dictionary<string, string> ContentTypesByExtension = new()
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".json"] = "application/json",
    };

    private readonly int _port;
    private readonly string _wwwrootPath;

    public StaticFileServer(int port)
    {
        _port = port;
        _wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    }

    public void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "openBCF static file server" };
        thread.Start();
    }

    private void Run()
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        listener.Start();

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
            }
            catch
            {
                return;
            }

            _ = HandleRequestAsync(context);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var requestPath = context.Request.Url?.AbsolutePath ?? "/";
            var relativePath = requestPath == "/" ? "index.html" : requestPath.TrimStart('/');
            var filePath = Path.GetFullPath(Path.Combine(_wwwrootPath, relativePath));

            // Refuse anything that escaped wwwroot via ../ - this server only ever needs to hand
            // out the frontend's own build output.
            if (!filePath.StartsWith(_wwwrootPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            var extension = Path.GetExtension(filePath);
            context.Response.ContentType = ContentTypesByExtension.GetValueOrDefault(extension, "application/octet-stream");

            var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort - a failed single request (client disconnected mid-response, etc.) must
            // not take the listener loop down.
        }
        finally
        {
            context.Response.Close();
        }
    }
}
