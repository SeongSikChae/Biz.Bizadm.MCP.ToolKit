using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using global::NuGet.Versioning;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.NuGet;

/// <summary>push할 <c>.nupkg</c>의 경로 검사·읽기와 <c>.nuspec</c>의 id·version 읽기</summary>
internal static class NuGetPackageFile
{
    /// <summary>패키지 크기 상한(nuget.org 업로드 상한과 같은 250MB)</summary>
    internal const long MaxPackageBytes = 250L * 1024 * 1024;

    /// <summary><c>.nuspec</c> 압축 해제 크기 상한(1MB, zip bomb 방지)</summary>
    internal const long MaxNuspecBytes = 1024 * 1024;

    private const string PathError = "path must be an absolute local file path; UNC and device paths are not allowed.";

    private static readonly Regex DrivePath = new(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);

    /// <summary>Windows는 드라이브 경로만, 그 밖은 <c>/</c>로 시작하고 <c>//</c>로 시작하지 않는 경로만 허용한다(UNC·장치 경로 거부).</summary>
    internal static bool IsLocalAbsolute(string path, bool windows) => windows
        ? DrivePath.IsMatch(path)
        : path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal);

    /// <summary>MCP 서버 호스트의 <c>.nupkg</c>를 읽는다. 파일에 접근하기 전에 경로 형태를 검사한다.</summary>
    /// <exception cref="McpException">경로 형태·확장자·존재·크기·읽기 오류</exception>
    public static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
    {
        bool windows = OperatingSystem.IsWindows();
        if (!IsLocalAbsolute(path, windows) || !IsLocalAbsolute(FullPath(path), windows))
            throw new McpException(PathError);
        if (!path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            throw new McpException("path must be a .nupkg file (.snupkg is not supported).");
        try
        {
            FileInfo file = new(path);
            if (!file.Exists)
                throw new McpException($"package file was not found: {path}");
            if (file.Length > MaxPackageBytes)
                throw new McpException($"package file exceeds {MaxPackageBytes} bytes.");
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new McpException($"package file could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>Base64 패키지를 디코딩한다.</summary>
    /// <exception cref="McpException">잘못된 Base64, 크기 초과</exception>
    public static byte[] Decode(string contentBase64)
    {
        string text = contentBase64.Trim();
        // 디코딩 전에 길이로 상한을 먼저 본다(메모리 보호)
        if ((long)text.Length / 4 * 3 > MaxPackageBytes + 3)
            throw new McpException($"package exceeds {MaxPackageBytes} bytes.");
        try
        {
            byte[] bytes = Convert.FromBase64String(text);
            return bytes.LongLength > MaxPackageBytes ? throw new McpException($"package exceeds {MaxPackageBytes} bytes.") : bytes;
        }
        catch (FormatException ex)
        {
            throw new McpException("contentBase64 is not valid Base64.", ex);
        }
    }

    /// <summary>루트 <c>.nuspec</c>에서 id와 정규화한 version을 읽는다.</summary>
    /// <exception cref="McpException">zip 아님, nuspec 없음·너무 큼·잘못된 XML, id·version 없음</exception>
    public static (string Id, string Version) ReadIdentity(byte[] package)
    {
        try
        {
            using ZipArchive zip = new(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
            ZipArchiveEntry nuspec = zip.Entries.FirstOrDefault(e => e.FullName.IndexOfAny(['/', '\\']) < 0
                    && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                ?? throw new McpException("package has no .nuspec at its root.");
            if (nuspec.Length > MaxNuspecBytes)
                throw new McpException($".nuspec exceeds {MaxNuspecBytes} bytes.");

            using Stream stream = nuspec.Open();
            // 헤더의 Length를 믿지 않고 실제 읽기도 제한한다
            using MemoryStream limited = new();
            byte[] chunk = new byte[8192];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (limited.Length + read > MaxNuspecBytes)
                    throw new McpException($".nuspec exceeds {MaxNuspecBytes} bytes.");
                limited.Write(chunk, 0, read);
            }
            limited.Position = 0;

            using XmlReader reader = XmlReader.Create(limited, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            XElement metadata = XDocument.Load(reader).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
                ?? throw new McpException(".nuspec has no metadata element.");
            string? Value(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

            string id = Value("id") is { Length: > 0 } i ? i : throw new McpException(".nuspec has no id.");
            string? rawVersion = Value("version");
            return NuGetVersion.TryParse(rawVersion, out NuGetVersion? version)
                ? (id, version.ToNormalizedString())
                : throw new McpException($".nuspec version '{rawVersion}' is not a valid NuGet version.");
        }
        catch (InvalidDataException ex)
        {
            throw new McpException($"package is not a valid .nupkg (zip): {ex.Message}", ex);
        }
        catch (XmlException ex)
        {
            throw new McpException($".nuspec is not valid XML: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException)
        {
            throw new McpException($"package could not be read: {ex.Message}", ex);
        }
    }

    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new McpException(PathError, ex);
        }
    }
}
