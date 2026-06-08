using System.Reflection;

namespace Assistant.App;

/// <summary>
/// 從 Embedded Resources 載入靜態網頁資產。
/// <para>
/// 命名規則：資源 <c>frontend/assets/index.js</c> 對應實體路徑
/// <c>{FrontendDistDir}/assets/index.js</c>，
/// 由 MSBuild EmbeddedResource LogicalName 在建置時決定。
/// </para>
/// </summary>
internal sealed class ResourceLoader
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    /// <summary>
    /// 根據資源路徑載入嵌入資源串流與對應的 Content-Type。
    /// </summary>
    /// <param name="resourcePath">
    /// 相對資源路徑，例如 <c>frontend/index.html</c> 或 <c>frontend/assets/index.js</c>
    /// </param>
    /// <returns>
    /// 找到時回傳 <c>(Stream, contentType)</c>；
    /// 找不到時回傳 <c>(null, null)</c>。
    /// </returns>
    public (Stream? Stream, string? ContentType) Load(string resourcePath)
    {
        var stream = _assembly.GetManifestResourceStream(resourcePath)
                     ?? _assembly.GetManifestResourceStream(resourcePath.Replace('/', '\\'));
        if (stream is null) return (null, null);

        var contentType = GetContentType(resourcePath);
        return (stream, contentType);
    }

    /// <summary>依副檔名對應 MIME Content-Type</summary>
    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js"   => "application/javascript; charset=utf-8",
            ".mjs"  => "application/javascript; charset=utf-8",
            ".css"  => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg"  => "image/svg+xml",
            ".png"  => "image/png",
            ".jpg"  => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".ico"  => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2"=> "font/woff2",
            ".ttf"  => "font/ttf",
            _       => "application/octet-stream",
        };
    }
}
