using System.IO.Compression;
using System.Text;

using Biz.Bizadm.MCP.ToolKit.NuGet;
using ModelContextProtocol;

namespace Biz.Bizadm.MCP.ToolKit.Tests.NuGet;

[TestClass]
public sealed class NuGetPackageFileTests
{
    public TestContext TestContext { get; set; } = null!;

    internal static byte[] Package(string? nuspec = """<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>My.Pkg</id><version>1.0+build.1</version></metadata></package>""",
        string entryName = "My.Pkg.nuspec")
    {
        using MemoryStream ms = new();
        using (ZipArchive zip = new(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("lib/net8.0/My.Pkg.dll");
            zip.CreateEntry("sub/Other.nuspec");
            if (nuspec is not null)
            {
                using StreamWriter w = new(zip.CreateEntry(entryName).Open(), Encoding.UTF8);
                w.Write(nuspec);
            }
        }
        return ms.ToArray();
    }

    [TestMethod]
    public void ReadIdentity_ReadsRootNuspecAndNormalizes()
        => Assert.AreEqual(("My.Pkg", "1.0.0"), NuGetPackageFile.ReadIdentity(Package()));

    [TestMethod]
    [DataRow(null, "My.Pkg.nuspec", "package has no .nuspec at its root.")]
    [DataRow("<package><metadata><id>A</id><version>1.0.0</version></metadata></package>", "dir\\My.Pkg.nuspec", "package has no .nuspec at its root.")]
    [DataRow("<package><metadata><version>1.0.0</version></metadata></package>", "A.nuspec", ".nuspec has no id.")]
    [DataRow("<package><metadata><id>A</id><version>x.y</version></metadata></package>", "A.nuspec", ".nuspec version 'x.y' is not a valid NuGet version.")]
    [DataRow("<package></package>", "A.nuspec", ".nuspec has no metadata element.")]
    public void ReadIdentity_Errors(string? nuspec, string entryName, string message)
        => Assert.AreEqual(message, Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.ReadIdentity(Package(nuspec, entryName))).Message);

    [TestMethod]
    public void ReadIdentity_NotZip_InvalidXml_Dtd()
    {
        StringAssert.StartsWith(Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.ReadIdentity([1, 2, 3])).Message, "package is not a valid .nupkg (zip): ");
        StringAssert.StartsWith(Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.ReadIdentity(Package("<package><metadata>"))).Message, ".nuspec is not valid XML: ");
        string dtd = """<?xml version="1.0"?><!DOCTYPE p [<!ENTITY x "y">]><package><metadata><id>&x;</id><version>1.0.0</version></metadata></package>""";
        StringAssert.StartsWith(Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.ReadIdentity(Package(dtd))).Message, ".nuspec is not valid XML: ");
    }

    [TestMethod]
    public void ReadIdentity_NuspecTooLarge()
    {
        string big = "<package><metadata><id>A</id><version>1.0.0</version><description>" + new string('a', (int)NuGetPackageFile.MaxNuspecBytes) + "</description></metadata></package>";
        Assert.AreEqual($".nuspec exceeds {NuGetPackageFile.MaxNuspecBytes} bytes.", Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.ReadIdentity(Package(big))).Message);
    }

    [TestMethod]
    [DataRow(@"C:\pkgs\a.nupkg", true, true)]
    [DataRow("c:/pkgs/a.nupkg", true, true)]
    [DataRow(@"\\host\share\a.nupkg", true, false)]
    [DataRow("//host/share/a.nupkg", true, false)]
    [DataRow(@"/\host\share\a.nupkg", true, false)]
    [DataRow(@"\/host/share/a.nupkg", true, false)]
    [DataRow(@"\\?\C:\a.nupkg", true, false)]
    [DataRow(@"\\.\C:\a.nupkg", true, false)]
    [DataRow(@"\??\UNC\host\share\a.nupkg", true, false)]
    [DataRow(@"pkgs\a.nupkg", true, false)]
    [DataRow("/home/u/a.nupkg", false, true)]
    [DataRow("//host/share/a.nupkg", false, false)]
    [DataRow("relative/a.nupkg", false, false)]
    [DataRow(@"C:\pkgs\a.nupkg", false, false)]
    public void IsLocalAbsolute_BothBranches(string path, bool windows, bool expected)
        => Assert.AreEqual(expected, NuGetPackageFile.IsLocalAbsolute(path, windows));

    [TestMethod]
    public async Task Read_InvalidPaths()
    {
        string root = OperatingSystem.IsWindows() ? @"C:\" : "/";
        StringAssert.StartsWith((await Assert.ThrowsExactlyAsync<McpException>(() => NuGetPackageFile.ReadAsync("relative.nupkg", TestContext.CancellationToken))).Message, "path must be an absolute local file path");
        StringAssert.StartsWith((await Assert.ThrowsExactlyAsync<McpException>(() => NuGetPackageFile.ReadAsync(root + "a\0b.nupkg", TestContext.CancellationToken))).Message, "path must be");
        Assert.AreEqual("path must be a .nupkg file (.snupkg is not supported).",
            (await Assert.ThrowsExactlyAsync<McpException>(() => NuGetPackageFile.ReadAsync(root + "a.snupkg", TestContext.CancellationToken))).Message);
        Assert.AreEqual("path must be a .nupkg file (.snupkg is not supported).",
            (await Assert.ThrowsExactlyAsync<McpException>(() => NuGetPackageFile.ReadAsync(root + "a.zip", TestContext.CancellationToken))).Message);
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nupkg");
        Assert.AreEqual($"package file was not found: {missing}",
            (await Assert.ThrowsExactlyAsync<McpException>(() => NuGetPackageFile.ReadAsync(missing, TestContext.CancellationToken))).Message);
    }

    [TestMethod]
    public async Task Read_ExistingFile()
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nupkg");
        byte[] bytes = Package();
        await File.WriteAllBytesAsync(file, bytes, TestContext.CancellationToken);
        try
        {
            CollectionAssert.AreEqual(bytes, await NuGetPackageFile.ReadAsync(file, TestContext.CancellationToken));
        }
        finally
        {
            File.Delete(file);
        }
    }

    internal const string SymbolsNuspec = """<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>My.Pkg</id><version>1.0.0</version><packageTypes><packageType name="SymbolsPackage" /></packageTypes></metadata></package>""";

    [TestMethod]
    public void ReadIdentity_Symbols_RequiresSymbolsPackageType()
    {
        Assert.AreEqual(("My.Pkg", "1.0.0"), NuGetPackageFile.ReadIdentity(Package(SymbolsNuspec), symbols: true));
        Assert.AreEqual(("My.Pkg", "1.0.0"), NuGetPackageFile.ReadIdentity(Package(SymbolsNuspec)));
        Assert.AreEqual(".nuspec has no SymbolsPackage packageType; use nuget_push_package for .nupkg.",
            Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.ReadIdentity(Package(), symbols: true)).Message);
        string dependency = SymbolsNuspec.Replace("SymbolsPackage", "Dependency", StringComparison.Ordinal);
        Assert.AreEqual(".nuspec has no SymbolsPackage packageType; use nuget_push_package for .nupkg.",
            Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.ReadIdentity(Package(dependency), symbols: true)).Message);
        StringAssert.StartsWith(Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.ReadIdentity([1, 2, 3], symbols: true)).Message, "package is not a valid .snupkg (zip): ");
    }

    [TestMethod]
    public async Task Read_Symbols_Extension()
    {
        string root = OperatingSystem.IsWindows() ? @"C:\" : "/";
        Assert.AreEqual("path must be a .snupkg file.",
            (await Assert.ThrowsExactlyAsync<McpException>(() => NuGetPackageFile.ReadAsync(root + "a.nupkg", TestContext.CancellationToken, symbols: true))).Message);
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".snupkg");
        byte[] bytes = Package(SymbolsNuspec);
        await File.WriteAllBytesAsync(file, bytes, TestContext.CancellationToken);
        try
        {
            CollectionAssert.AreEqual(bytes, await NuGetPackageFile.ReadAsync(file, TestContext.CancellationToken, symbols: true));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void Decode_Errors()
    {
        Assert.AreEqual("contentBase64 is not valid Base64.", Assert.ThrowsExactly<McpException>(() => NuGetPackageFile.Decode("not base64!")).Message);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, NuGetPackageFile.Decode(" AQID "));
    }
}
