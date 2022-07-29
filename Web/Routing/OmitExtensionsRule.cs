using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Net.Http.Headers;
using Rumble.Platform.Common.Utilities;

namespace Rumble.Platform.Common.Web.Routing;

public class OmitExtensionsRule : PlatformRewriteRule
{
    internal static readonly string[] EXTENSIONS = { ".html", ".php", ".aspx", ".asp" };
    private static readonly int MAX_LENGTH = EXTENSIONS.Max(selector: extension => extension.Length);

    protected override RuleResult Apply(HttpRequest request, HttpResponse response)
    {
        string path = request.Path.Value;

        int length = Math.Min(MAX_LENGTH, path.Length);
        int index = Array.IndexOf(EXTENSIONS, path[^length..]);

        if (index < 0)  // The extension is not in the recognized extension list, therefore this rule doesn't apply.
            return default;

        response.StatusCode = (int) HttpStatusCode.PermanentRedirect;
        response.Headers[HeaderNames.Location] = path[..^EXTENSIONS[index].Length];
        return RuleResult.EndResponse;
    }
}