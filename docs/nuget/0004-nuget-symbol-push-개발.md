---
status: done
category: nuget
feature: nuget-symbol-push
phase: 개발
---

# nuget-symbol-push 개발

단축(바로 구현). 선행 문서 없음. 기존 기능 문서: `0001~0003-nuget-tools-*`. GitHub 이슈 #5.

## 배경·결정

- nuget.org service index에서 `.nupkg`는 `PackagePublish/2.0.0`(`https://www.nuget.org/api/v2/package`), `.snupkg`는 `SymbolPackagePublish/4.9.0`(`https://www.nuget.org/api/v2/symbolpackage`)으로 받는다. 둘 다 `X-NuGet-ApiKey`와 `PUT` multipart(`package` 필드)를 쓴다.
- 새 Tool `nuget_push_symbol_package`를 추가한다(사용자 결정). `nuget_push_package`의 인자·결과·`.snupkg` 거부는 바꾸지 않는다.
- 심볼 Tool은 `.snupkg`만 받는다. path는 확장자 `.snupkg`, 두 입력 모두 `.nuspec`의 `packageTypes/packageType@name`에 `SymbolsPackage`가 있어야 한다. 일반 `.nupkg`를 심볼 endpoint로 보내지 않기 위함이다.
- nuget.org 심볼 push 의미(공식 문서 Push Symbol Packages): 같은 id·version의 심볼은 여러 번 올릴 수 있고(재업로드 허용), HTTP 409는 "이미 있음"이 아니라 "같은 id·version의 심볼을 올렸지만 아직 사용 가능하지 않음(검증 중)"이다. 심볼은 대응하는 `.nupkg`를 먼저 올린 뒤 올린다. 따라서 심볼 Tool의 409 메시지는 "already exists"가 아니라 "was pushed but is not available yet"로 쓰고, `skipDuplicate`(인자 이름은 `nuget_push_package`와 같게 유지)는 이 409를 `skipped=true`로 처리한다고 Description에 적는다. nupkg보다 먼저 올렸을 때의 400·404 등은 기존 `SendPublishAsync` 실패 메시지(HTTP 코드·사유)로 그대로 보고한다.
- 비범위: unlist·relist의 심볼 지원, nupkg+snupkg 동시 push, `SymbolPackagePublish`가 없는 피드의 대체 경로.

## 단위 1. service index 심볼 리소스

**대상**: `NuGet/NuGetServiceIndex.cs`(`NuGetResource`, `Map`), `NuGet/NuGetFeedClient.cs`(`GetResourceAsync`의 https 하향 검사)

**코드 초안**

```csharp
internal enum NuGetResource { Search, PackageBase, Registrations, Publish, SymbolPublish }

[NuGetResource.SymbolPublish] = ("symbol publish", ["SymbolPackagePublish/4.9.0"]),
```

```csharp
// ApiKey를 보내는 리소스는 모두 https 하향을 막는다
if ((resource is NuGetResource.Publish or NuGetResource.SymbolPublish)
    && _origins[instance.Name].Scheme == Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttps)
    throw new McpException($"NuGet '{instance.Name}' {NuGetServiceIndex.NameOf(resource)} resource is not https; ApiKey would be sent in clear text.");
```

기존 메시지 `publish resource is not https`는 `NameOf(Publish)`가 `publish`라 그대로 유지된다.

주석 갱신: `NuGetFeedClient` 클래스 remarks(15줄)·`GetResourceAsync` exception(82줄)·88줄 주석·`SendPublishAsync` summary(153줄)의 "PackagePublish"/"publish"를 "PackagePublish·SymbolPackagePublish"(ApiKey를 보내는 publish 계열)로 고친다.

**검증**: `NuGetFeedClientTests`에 `SymbolPackagePublish/4.9.0` 조회, 리소스 없음 메시지, https 인스턴스의 http 주소 거부를 Publish·SymbolPublish 두 경우 모두(`symbol publish resource is not https`) 테스트. 기존 테스트 통과.

**선행 단위**: 없음

## 단위 2. 심볼 패키지 파일 검사

**대상**: `NuGet/NuGetPackageFile.cs`(`ReadAsync`, `ReadIdentity`)

**코드 초안**

```csharp
public static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken, bool symbols = false)
{
    // ... 경로 형태 검사(기존)
    if (symbols ? !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)
                : !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
        throw new McpException(symbols ? "path must be a .snupkg file." : "path must be a .nupkg file (.snupkg is not supported).");
    // ... 기존 읽기
}

public static (string Id, string Version) ReadIdentity(byte[] package, bool symbols = false)
{
    // ... metadata 읽기(기존)
    if (symbols && !metadata.Elements().Where(e => e.Name.LocalName == "packageTypes").Elements()
            .Any(e => e.Name.LocalName == "packageType" && string.Equals((string?)e.Attribute("name"), "SymbolsPackage", StringComparison.OrdinalIgnoreCase)))
        throw new McpException(".nuspec has no SymbolsPackage packageType; use nuget_push_package for .nupkg.");
    // ... id·version(기존)
}
```

```csharp
catch (InvalidDataException ex)
{
    throw new McpException($"package is not a valid {(symbols ? ".snupkg" : ".nupkg")} (zip): {ex.Message}", ex);
}
```

기본값 `symbols = false`이므로 기존 호출(`nuget_push_package`)의 동작·메시지는 그대로다.

**검증**: `NuGetPackageFileTests`에 `.snupkg` 경로 허용/`.nupkg` 거부(symbols), `SymbolsPackage` 있음/없음, symbols의 zip 오류 문구 `.snupkg (zip)` 테스트. 기존 테스트(`.nupkg (zip)` 문구 포함) 통과.

**선행 단위**: 없음

## 단위 3. `nuget_push_symbol_package` Tool

**대상**: `Tools/NuGet/NuGetTools.cs`(`PushPackage` 본문을 공통 `PushAsync`로 추출, `PushSymbolPackage` 추가)

**코드 초안**

```csharp
[McpServerTool(Name = "nuget_push_symbol_package", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
[Description("지정한 NuGet 피드의 심볼 리소스(SymbolPackagePublish/4.9.0)에 .snupkg를 올리고 {pushed{id, version}, skipped}를 반환한다. path(MCP 서버 호스트의 절대 로컬 경로, UNC·장치 경로 불가)와 contentBase64 중 정확히 하나를 준다. "
    + "패키지 상한은 250MB이고 .nuspec에 SymbolsPackage packageType이 있어야 한다. 대응하는 .nupkg를 먼저 올린다. 같은 버전은 다시 올릴 수 있고, HTTP 409(이전 심볼이 아직 사용 가능하지 않음)는 오류이며 skipDuplicate=true면 skipped=true로 성공 처리한다. "
    + "피드에 심볼 리소스가 없으면 오류다. 인스턴스에 ApiKey가 필요하다.")]
public Task<string> PushSymbolPackage(
    [Description(InstanceDescription)] string? instance = null,
    [Description("MCP 서버 호스트의 .snupkg 절대 경로(path·contentBase64 중 하나)")] string? path = null,
    [Description(".snupkg 내용 Base64(path·contentBase64 중 하나)")] string? contentBase64 = null,
    [Description("HTTP 409(이전 심볼 검증 중)를 성공으로 볼지 여부(기본 false)")] bool skipDuplicate = false,
    CancellationToken cancellationToken = default)
    => PushAsync(instance, path, contentBase64, skipDuplicate, symbols: true, cancellationToken);

private async Task<string> PushAsync(string? instance, string? path, string? contentBase64, bool skipDuplicate, bool symbols, CancellationToken cancellationToken)
{
    // ... RequireWritable, path/contentBase64 검사(기존)
    byte[] bytes = path is set ? await NuGetPackageFile.ReadAsync(path.Trim(), cancellationToken, symbols) : NuGetPackageFile.Decode(contentBase64!);
    (string id, string version) = NuGetPackageFile.ReadIdentity(bytes, symbols);
    Uri publish = await _client.GetResourceAsync(target, symbols ? NuGetResource.SymbolPublish : NuGetResource.Publish, cancellationToken);
    form.Add(file, "package", symbols ? "package.snupkg" : "package.nupkg");
    if (status == 409 && !skipDuplicate)
        throw new McpException(symbols
            ? $"NuGet '{target.Name}' symbol package '{id}' {version} was pushed but is not available yet (HTTP 409): {why}. Use skipDuplicate to ignore."
            : $"NuGet '{target.Name}' package '{id}' {version} already exists (HTTP 409): {why}. Use skipDuplicate to ignore."); // 기존 문구 유지
    // ... 결과 JSON(기존)
}
```

**검증**: `NuGetToolsWriteTests`에 심볼 push(Base64·path) PUT 주소·ApiKey·filename, 409 실패/skip, 심볼 리소스 없는 피드 오류, `.nupkg`/SymbolsPackage 없음 거부(요청 없음), ApiKey 없음. 기존 push 테스트 통과. `NuGetRegistrationTests.WithNuGetTools_RegistersEightToolsWithAnnotations`를 `...RegistersNineToolsWithAnnotations`로 바꾸고, 이름 목록(42-46줄)에 `nuget_push_symbol_package`를 넣고, 쓰기 Tool annotation 목록(54줄)과 `DestructiveHint`·`IdempotentHint` false 검사에 새 Tool을 추가한다.

**선행 단위**: 1, 2

## 단위 4. README

**대상**: `README.md` NuGet 절 Tool 표·push 설명

**코드 초안**

```markdown
| `nuget_push_symbol_package` | `path` 또는 `contentBase64`(정확히 하나), `skipDuplicate`(기본 false) | `PUT {symbol publish}` multipart `package` | 쓰기 · 외부 접근 | `{pushed{id, version}, skipped}` |
```

ApiKey 전송 범위 설명(178줄 "PackagePublish 요청에만 `X-NuGet-ApiKey`로 보낸다", 185줄 "ApiKey는 service index가 알려 준 PackagePublish 주소로 보낸다… publish 주소가 http면 요청 전 오류")을 PackagePublish·SymbolPackagePublish 주소로 고친다. NuGet 절 첫 문단 리소스 목록(1931줄 "쓰기 `PackagePublish/2.0.0`")에 `SymbolPackagePublish/4.9.0`(심볼 쓰기)을 추가한다. push 설명의 `.snupkg는 지원하지 않는다`를 `nuget_push_package`에 한정하고 심볼 Tool 조건(SymbolPackagePublish/4.9.0, SymbolsPackage packageType)을 적는다.

**검증**: 문서 내용이 구현 메시지·인자와 일치하는지 독립 검토.

**선행 단위**: 3
