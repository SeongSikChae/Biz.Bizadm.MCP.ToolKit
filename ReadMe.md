# Biz.Bizadm.MCP.ToolKit

ASP.NET Core MCP Server에 `WithTools`로 등록하는 Tool 모음 클래스 라이브러리입니다.

- 대상 프레임워크: `net10.0`
- 의존 패키지: [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) 2.3.1, [`Microsoft.Management.Infrastructure`](https://www.nuget.org/packages/Microsoft.Management.Infrastructure)(Storage Tool의 CIM 호출)
- 전송 방식(HTTP, stdio)과 무관하며, 전송 설정은 서버 쪽에서 합니다.

## 사용법

### 패키지 참조

```xml
<PackageReference Include="Biz.Bizadm.MCP.ToolKit" Version="1.3.1" />
```

### 개별 Tool 등록

Tool 클래스는 `public`이므로 필요한 것만 골라 등록할 수 있습니다.

```csharp
using Biz.Bizadm.MCP.ToolKit.Tools;

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<AmpTools>()
    .WithTools<AudioLevelTools>()
    .WithTools<SpeakerTools>()
    .WithTools<StorageTools>();
```

### AgentResource Tool 등록

rule·skill·tool·docs 문서 Tool은 문서 루트 옵션과 함께 한 번에 등록합니다(4개 클래스, Tool 21개).

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithAgentResourceTools(o =>
    {
        o.RootPath = @"H:\.agents"; // 절대 경로. 아래에 rules, skills, tools, docs 폴더를 둔다
    });
```

| 옵션 | 설명 |
| --- | --- |
| `RootPath` | 문서 루트 폴더 절대 경로. 비었거나 상대 경로면 `ArgumentException` |

### Redmine Tool 등록

Redmine REST API Tool은 접속 옵션과 함께 한 번에 등록합니다. 응답은 Redmine JSON 원문 문자열입니다.

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithRedmineTools(o =>
    {
        o.ApiEndpoint = "https://redmine.example/redmine";
        o.ApiKey = "<API 키>";
        o.Version = RedmineVersion.V7_0;                  // 기본 V2_2
        o.TextFormatting = RedmineTextFormatting.Textile; // 서버 텍스트 서식과 같게. 기본 Textile
    });
```

| 옵션 | 값 | 설명 |
| --- | --- | --- |
| `ApiEndpoint` | 절대 http/https URI | Redmine 서버 주소 |
| `ApiKey` | 문자열 | Redmine API 키. 비면 `ArgumentException` |
| `Version` | `V2_2`(기본), `V7_0` | 대상 서버 버전. `V2_2`는 Tool 27개, `V7_0`은 61개를 등록한다 |
| `TextFormatting` | `Textile`(기본), `CommonMark` | 서버의 "관리 → 설정 → 일반 → 텍스트 서식"과 같은 값. `get_text_guide`가 이 서식의 문법을 반환한다. `V2_2`에서는 `Textile`만 쓸 수 있다 |

### Jenkins Tool 등록

Jenkins MCP Server 플러그인(`/mcp-server/mcp`)을 여러 대 등록하고, 게이트웨이 Tool 3개로 인스턴스를 골라 중계합니다. Jenkins 쪽 Tool 목록은 인스턴스마다 다를 수 있으므로 `jenkins_list_tools`로 해당 인스턴스에서 직접 조회합니다.

```csharp
using Biz.Bizadm.MCP.ToolKit.Jenkins;

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithJenkinsTools(o =>
    {
        o.Instances.Add(new JenkinsInstance
        {
            Name = "prod",
            Endpoint = "http://jenkins.example:8080/mcp-server/mcp",
            User = "builder",
            ApiToken = "<API Token>",
            Description = "운영 빌드",
        });
        o.Instances.Add(new JenkinsInstance
        {
            Name = "dev",
            Endpoint = "http://jenkins-dev.example:8080/mcp-server/mcp",
            User = "builder",
            ApiToken = "<API Token>",
            Description = "개발 빌드",
        });
    });
```

| 옵션(`JenkinsInstance`) | 설명 |
| --- | --- |
| `Name` | 인스턴스 이름. Tool의 `instance` 인자 값(대소문자 무시). 비거나 중복이면 `ArgumentException` |
| `Endpoint` | Jenkins MCP 주소 전체(절대 http/https URI). `user:token@` 같은 사용자 정보를 넣으면 `ArgumentException` |
| `User`, `ApiToken` | Basic 인증용 Jenkins 사용자와 API Token. API Token으로 인증하면 crumb이 필요 없다 |
| `Description` | 용도 설명(선택). `jenkins_list_instances`에 노출된다 |

- 인스턴스는 1개 이상 등록해야 하며 기본 인스턴스는 없다. 모든 호출에 `instance`가 필요하다.
- 전송은 Streamable HTTP만 쓴다(SSE 미지원).
- API Token 암복호는 소비 서버 책임이다. 등록한 값은 그대로 Authorization 헤더에 쓰인다.

### GitHub Tool 등록

GitHub MCP Server(원격 `https://api.githubcopilot.com/mcp/` 또는 자체 호스팅 `github-mcp-server` HTTP 모드)를 여러 대 등록하고, 게이트웨이 Tool 3개로 인스턴스를 골라 중계합니다. 동작은 Jenkins 중계와 같습니다. GitHub MCP Server가 노출하지 않는 태그·ref 쓰기와 릴리스 쓰기는 GitHub REST API를 직접 부르는 Tool(태그·ref 4개, 릴리스 5개)로 함께 등록됩니다.

```csharp
using Biz.Bizadm.MCP.ToolKit.GitHub;

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithGitHubTools(o =>
    {
        o.Instances.Add(new GitHubInstance
        {
            Name = "public",
            Endpoint = "https://api.githubcopilot.com/mcp/",
            ApiEndpoint = "https://api.github.com/",
            Token = "<Personal Access Token>",
            Description = "github.com 조직 저장소",
        });
    });
```

| 옵션(`GitHubInstance`) | 설명 |
| --- | --- |
| `Name` | 인스턴스 이름. Tool의 `instance` 인자 값(대소문자 무시). 비거나 중복이면 `ArgumentException` |
| `Endpoint` | GitHub MCP 주소 전체(절대 http/https URI). `user:token@` 같은 사용자 정보를 넣으면 `ArgumentException` |
| `ApiEndpoint` | **필수**(1.4.0~). GitHub REST API 기본 주소. github.com은 `https://api.github.com/`, GitHub Enterprise는 `https://{host}/api/v3/`. 끝 `/`는 없으면 붙인다. 태그·ref Tool이 쓴다 |
| `Token` | Bearer 인증용 GitHub Personal Access Token. 게이트웨이와 태그·ref Tool이 함께 쓴다. 앞뒤 공백은 지운다 |
| `Description` | 용도 설명(선택). `github_list_instances`에 노출된다 |

- 인스턴스는 1개 이상 등록해야 하며 기본 인스턴스는 없다. 모든 호출에 `instance`가 필요하다.
- 전송은 Streamable HTTP만 쓴다(SSE 미지원).
- 노출되는 GitHub Tool 범위는 Endpoint 경로로 정한다(예: 기본 `/mcp/`, 전체 `/mcp/x/all`). `X-MCP-Toolsets` 등 추가 헤더는 지원하지 않는다. fine-grained PAT 권한은 Tool 목록을 거르지 않고 호출 시 upstream이 거부한다.
- GitHub 원격 MCP는 `owner`·`repo` 같은 인자를 `Mcp-Param-*` 헤더로도 요구한다. ToolKit은 연결마다 첫 호출 전에 `tools/list`를 받아 SDK가 이 헤더를 붙이게 하고, upstream이 `-32020`(헤더 불일치)을 돌려주면 목록을 다시 받아 한 번만 재시도한다.
- Token 암복호는 소비 서버 책임이다.
- **호환성(1.4.0)**: `ApiEndpoint`가 필수가 되어, 1.3.x 설정에 `ApiEndpoint`가 없으면 등록 시 `ArgumentException`이다.

## 제공 Tool

| 분류 | Tool 클래스 | 등록 방법 | Tool 수 |
| --- | --- | --- | --- |
| 앰프 계산 | `Tools.AmpTools` | `WithTools<AmpTools>()` | 2 |
| 오디오 레벨 환산 | `Tools.AudioLevelTools` | `WithTools<AudioLevelTools>()` | 4 |
| 스피커 SPL 계산 | `Tools.SpeakerTools` | `WithTools<SpeakerTools>()` | 1 |
| Windows 저장소 공간 | `Tools.StorageTools` | `WithTools<StorageTools>()` | 11 |
| AgentResource 문서 | `Tools.AgentResource.*` 4개 | `WithAgentResourceTools(...)` | 21 |
| Redmine | `Tools.Redmine.*`, `Tools.Redmine.V7.*` | `WithRedmineTools(...)` | V2_2: 27, V7_0: 61 |
| Jenkins MCP 중계 | `Tools.Jenkins.JenkinsTools` | `WithJenkinsTools(...)` | 3 |
| GitHub MCP 중계 | `Tools.GitHub.GitHubTools` | `WithGitHubTools(...)` | 3 |
| GitHub 태그·ref (REST) | `Tools.GitHub.GitHubRefTools` | `WithGitHubTools(...)` | 4 |
| GitHub 릴리스 (REST) | `Tools.GitHub.GitHubReleaseTools` | `WithGitHubTools(...)` | 5 |

각 Tool 명세의 표기:

- 힌트: MCP Tool annotation. `읽기 전용`(ReadOnly), `쓰기`, `멱등`(Idempotent), `파괴적`(Destructive), `외부 접근`(OpenWorld)
- 반환: `structured content`는 결과 타입을 JSON 구조화 결과로 반환한다. `문자열`은 텍스트 결과다(Redmine Tool은 Redmine JSON 원문)
- 필수: ✓ 표시가 없는 파라미터는 생략할 수 있다

### 앰프 계산 — `Tools.AmpTools`

#### `calculate_amp_max_output`

앰프의 임피던스(Ω), 정격 출력(W), 입력 감도(Vrms)로 입력 감도(Vpp), 최대 출력 전압(Vrms/Vpp), 전압 이득(dB)을 계산한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `AmpMaxOutputResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `impedanceOhm` | number | ✓ |  | 스피커 임피던스(Ω) |
| `powerW` | number | ✓ |  | 정격 출력(W) |
| `inputSensitivityVrms` | number | ✓ |  | 입력 감도(Vrms) |

#### `calculate_amp_load_output`

입력 전압(Vrms), 입력 감쇠(dB), 스피커 임피던스(Ω), 앰프 전압 이득(dB), 최대 출력 전압(Vrms)으로 실질 입력 전압, 출력 전압(Vrms/Vpp), 출력 전력(W)을 계산한다. 출력 전압은 최대 출력 전압으로 제한한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `AmpLoadOutputResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `inputVrms` | number | ✓ |  | 입력 전압(Vrms) |
| `inputAttenuationDb` | number | ✓ |  | 입력 감쇠(dB), 0 이상, 양수가 감쇠 |
| `impedanceOhm` | number | ✓ |  | 스피커 임피던스(Ω) |
| `voltageGainDb` | number | ✓ |  | 앰프 전압 이득(dB), calculate_amp_max_output의 voltageGainDb |
| `maxOutputVrms` | number | ✓ |  | 앰프 최대 출력 전압(Vrms), calculate_amp_max_output의 maxOutputVrms |

### 오디오 레벨 환산 — `Tools.AudioLevelTools`

#### `convert_vrms_to_dbu`

전압(Vrms)을 dBu로 환산한다. 0dBu 기준은 Nominal(0.775V, 기본) 또는 Exact(√0.6V)이다.

- 힌트: 읽기 전용 · 멱등
- 반환: `VrmsToDbuResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `vrms` | number | ✓ |  | 전압(Vrms) |
| `reference` | string (`Nominal`, `Exact`) |  | `Nominal` | 0dBu 기준: Nominal(0.775V, 기본) 또는 Exact(√0.6V) |

#### `convert_dbu_to_vrms`

dBu를 전압(Vrms)으로 환산한다. 0dBu 기준은 Nominal(0.775V, 기본) 또는 Exact(√0.6V)이다.

- 힌트: 읽기 전용 · 멱등
- 반환: `DbuToVrmsResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `dbu` | number | ✓ |  | 레벨(dBu) |
| `reference` | string (`Nominal`, `Exact`) |  | `Nominal` | 0dBu 기준: Nominal(0.775V, 기본) 또는 Exact(√0.6V) |

#### `convert_dbu_to_dbfs`

0dBFS에 해당하는 기준 dBu로 레벨(dBu)을 dBFS로 환산한다. 0dBFS를 넘는 레벨(클리핑)은 거부한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `DbuToDbfsResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `fullScaleDbu` | number | ✓ |  | 0dBFS에 해당하는 기준 레벨(dBu), 예: +24 |
| `dbu` | number | ✓ |  | 환산할 레벨(dBu), fullScaleDbu 이하 |

#### `convert_dbfs_to_dbu`

0dBFS에 해당하는 기준 dBu로 디지털 레벨(dBFS)을 dBu로 환산한다. 0dBFS를 넘는 레벨(클리핑)은 거부한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `DbfsToDbuResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `fullScaleDbu` | number | ✓ |  | 0dBFS에 해당하는 기준 레벨(dBu), 예: +24 |
| `dbfs` | number | ✓ |  | 환산할 디지털 레벨(dBFS), 0 이하 |

### 스피커 SPL 계산 — `Tools.SpeakerTools`

#### `calculate_speaker_spl`

스피커 Nominal SPL(dB SPL, 1W/1m 감도), 입력 전력(W), 거리(m), 음원 유형(Point/Line)으로 해당 거리의 Output SPL(dB SPL)을 계산한다. Point는 거리 2배당 −6dB, Line은 −3dB 감쇠한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `SpeakerSplResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `nominalSplDb` | number | ✓ |  | Nominal SPL(dB SPL), 1W/1m 감도 |
| `inputPowerW` | number | ✓ |  | 입력 전력(W), calculate_amp_load_output의 outputPowerW |
| `distanceM` | number | ✓ |  | 거리(m) |
| `sourceType` | string (`Point`, `Line`) | ✓ |  | 음원 유형: Point(점음원, 역제곱) 또는 Line(라인어레이) |

### Windows 저장소 공간 — `Tools.StorageTools`

로컬 Windows의 저장소 공간(Storage Spaces)·디스크·파티션·볼륨을 CIM(`root/Microsoft/Windows/Storage`)으로 다룬다. Windows가 아니면 오류를 반환한다.

#### `list_storage_pools`

로컬 Windows의 저장소 공간(Storage Spaces) 저장소 풀 목록을 조회한다(Get-StoragePool). Windows가 아니면 오류를 반환한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `StoragePoolListResult` (structured content)

파라미터 없음

#### `get_storage_pool`

로컬 Windows의 저장소 풀 1개를 friendlyName 또는 uniqueId 중 하나로 조회한다. 없거나 이름이 여러 풀과 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `StoragePoolInfo` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `friendlyName` | string |  |  | 저장소 풀 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 저장소 풀 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |

#### `list_virtual_disks`

로컬 Windows 저장소 풀 1개에 속한 저장소 공간(가상 디스크) 목록을 조회한다(Get-VirtualDisk -StoragePool). 풀은 friendlyName 또는 uniqueId 중 하나로 지정한다. 풀이 없거나 이름이 여러 풀과 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `VirtualDiskListResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `friendlyName` | string |  |  | 저장소 풀 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 저장소 풀 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |

#### `get_virtual_disk`

로컬 Windows의 저장소 공간(가상 디스크) 1개를 friendlyName 또는 uniqueId 중 하나로 조회한다(Get-VirtualDisk). 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `VirtualDiskInfo` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `friendlyName` | string |  |  | 가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |

#### `create_virtual_disk`

로컬 Windows 저장소 풀 1개에 저장소 공간(가상 디스크)을 만든다(New-VirtualDisk). 풀은 storagePoolFriendlyName 또는 storagePoolUniqueId 중 하나로 지정하고, size 또는 useMaximumSize 중 하나를 지정한다. 지정하지 않은 선택 인자는 풀 기본값을 따른다. 풀이 없거나 이름이 여러 풀과 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.

- 힌트: 쓰기
- 반환: `VirtualDiskInfo` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `friendlyName` | string | ✓ |  | 새 가상 디스크 이름(FriendlyName) |
| `storagePoolFriendlyName` | string |  |  | 저장소 풀 이름(StoragePoolFriendlyName), storagePoolUniqueId와 함께 쓰지 않는다 |
| `storagePoolUniqueId` | string |  |  | 저장소 풀 고유 ID(StoragePoolUniqueId), storagePoolFriendlyName과 함께 쓰지 않는다 |
| `size` | integer |  |  | 크기(바이트, Size), useMaximumSize와 함께 쓰지 않는다 |
| `useMaximumSize` | boolean |  | `false` | 풀의 최대 크기 사용(UseMaximumSize), size와 함께 쓰지 않는다 |
| `resiliencySettingName` | string |  |  | 복원력 설정 이름(ResiliencySettingName), 예: Simple, Mirror, Parity |
| `provisioningType` | string (`Unknown`, `Thin`, `Fixed`) |  |  | 프로비저닝 유형(ProvisioningType) |
| `usage` | string (`Unknown`, `Other`, `Unrestricted`, `ReservedForComputerSystem`, `ReservedByReplicationServices`, `ReservedByMigrationServices`, `LocalReplicaSource`, `RemoteReplicaSource`, `LocalReplicaTarget`, `RemoteReplicaTarget`, `LocalReplicaSourceOrTarget`, `RemoteReplicaSourceOrTarget`, `DeltaReplicaTarget`, `ElementComponent`, `ReservedAsPoolContributor`, `CompositeVolumeMember`, `CompositeVirtualDiskMember`, `ReservedForSparing`, `Configuration`, `Data`, `Journal`, `ReadCache`, `CacheLog`, `DirtyRegionTracking`, `StripeStateTracking`, `Snapshot`, `Simple`, `ValidDataTracking`, `CacheLines`) |  |  | 용도(Usage) |
| `otherUsageDescription` | string |  |  | 기타 용도 설명(OtherUsageDescription), usage와 함께 지정한다 |
| `allocationUnitSize` | integer |  |  | 할당 단위 크기(바이트, AllocationUnitSize) |
| `mediaType` | string (`Unspecified`, `HDD`, `SSD`, `SCM`) |  |  | 미디어 유형(MediaType) |
| `isEnclosureAware` | boolean |  |  | 인클로저 인식 여부(IsEnclosureAware) |
| `faultDomainAwareness` | string (`PhysicalDisk`, `StorageEnclosure`, `StorageScaleUnit`, `StorageChassis`, `StorageRack`) |  |  | 장애 도메인 인식 단위(FaultDomainAwareness) |
| `columnIsolation` | string (`PhysicalDisk`, `StorageEnclosure`, `StorageScaleUnit`, `StorageChassis`, `StorageRack`) |  |  | 컬럼 격리 단위(ColumnIsolation) |
| `physicalDiskRedundancy` | integer |  |  | 허용 물리 디스크 장애 수(PhysicalDiskRedundancy) |
| `numberOfDataCopies` | integer |  |  | 데이터 복사본 수(NumberOfDataCopies) |
| `numberOfColumns` | integer |  |  | 컬럼 수(NumberOfColumns), autoNumberOfColumns와 함께 쓰지 않는다 |
| `autoNumberOfColumns` | boolean |  | `false` | 컬럼 수 자동 결정(AutoNumberOfColumns), numberOfColumns와 함께 쓰지 않는다 |
| `numberOfGroups` | integer |  |  | 그룹 수(NumberOfGroups) |
| `interleave` | integer |  |  | 인터리브(바이트, Interleave) |
| `writeCacheSize` | integer |  |  | 쓰기 캐시 크기(바이트, WriteCacheSize), autoWriteCacheSize와 함께 쓰지 않는다 |
| `autoWriteCacheSize` | boolean |  | `false` | 쓰기 캐시 크기 자동 결정(AutoWriteCacheSize), writeCacheSize와 함께 쓰지 않는다 |
| `readCacheSize` | integer |  |  | 읽기 캐시 크기(바이트, ReadCacheSize) |
| `timeoutSeconds` | integer |  | `300` | 저장소 job으로 진행될 때 완료를 기다리는 제한시간(초), 초과해도 job은 계속 진행된다 |

#### `resize_virtual_disk`

로컬 Windows의 저장소 공간(가상 디스크) 1개의 크기를 바꾼다(Resize-VirtualDisk). 대상은 friendlyName 또는 uniqueId 중 하나로 지정한다. 크기 축소 등 허용되지 않는 크기는 CIM 오류를 반환한다. 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.

- 힌트: 쓰기 · 멱등
- 반환: `VirtualDiskInfo` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `size` | integer | ✓ |  | 새 크기(바이트), 0보다 커야 한다 |
| `friendlyName` | string |  |  | 가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |
| `timeoutSeconds` | integer |  | `300` | 저장소 job으로 진행될 때 완료를 기다리는 제한시간(초), 초과해도 job은 계속 진행된다 |

#### `set_virtual_disk`

로컬 Windows의 저장소 공간(가상 디스크) 1개의 속성을 바꾼다(Set-VirtualDisk). 대상은 friendlyName 또는 uniqueId 중 하나로 지정하고, 바꿀 속성을 하나 이상 지정한다. 용도 → 속성(isManualAttach·storageNodeName·access) → 이름 순서로 적용하며, 중간에 실패하면 앞서 적용한 변경은 되돌리지 않는다. 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.

- 힌트: 쓰기 · 멱등
- 반환: `VirtualDiskInfo` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `friendlyName` | string |  |  | 가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |
| `newFriendlyName` | string |  |  | 새 이름(NewFriendlyName) |
| `usage` | string (`Unknown`, `Other`, `Unrestricted`, `ReservedForComputerSystem`, `ReservedByReplicationServices`, `ReservedByMigrationServices`, `LocalReplicaSource`, `RemoteReplicaSource`, `LocalReplicaTarget`, `RemoteReplicaTarget`, `LocalReplicaSourceOrTarget`, `RemoteReplicaSourceOrTarget`, `DeltaReplicaTarget`, `ElementComponent`, `ReservedAsPoolContributor`, `CompositeVolumeMember`, `CompositeVirtualDiskMember`, `ReservedForSparing`, `Configuration`, `Data`, `Journal`, `ReadCache`, `CacheLog`, `DirtyRegionTracking`, `StripeStateTracking`, `Snapshot`, `Simple`, `ValidDataTracking`, `CacheLines`) |  |  | 용도(Usage) |
| `otherUsageDescription` | string |  |  | 기타 용도 설명(OtherUsageDescription), usage와 함께 지정한다 |
| `isManualAttach` | boolean |  |  | 수동 연결 여부(IsManualAttach) |
| `storageNodeName` | string |  |  | 저장소 노드 이름(StorageNodeName) |
| `access` | string (`Unknown`, `Readable`, `Writeable`, `ReadWrite`, `WriteOnce`) |  |  | 접근 유형(Access) |

#### `remove_virtual_disk`

로컬 Windows의 저장소 공간(가상 디스크) 1개를 삭제한다(Remove-VirtualDisk). 가상 디스크의 데이터가 영구 삭제된다. confirm=true가 필요하다. 대상은 friendlyName 또는 uniqueId 중 하나로 지정한다. 없거나 이름이 여러 가상 디스크와 일치하면 오류를 반환한다. Windows가 아니면 오류를 반환한다.

- 힌트: 쓰기 · 파괴적
- 반환: `VirtualDiskRemoveResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `friendlyName` | string |  |  | 가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |
| `confirm` | boolean |  | `false` | 삭제를 확인한다. true가 아니면 삭제하지 않고 오류를 반환한다 |
| `timeoutSeconds` | integer |  | `300` | 저장소 job으로 진행될 때 완료를 기다리는 제한시간(초), 초과해도 job은 계속 진행된다 |

#### `initialize_disk`

로컬 Windows 저장소 공간(가상 디스크)의 디스크를 GPT 또는 MBR로 초기화한다(Initialize-Disk). 초기화되지 않은(RAW) 디스크만 허용하며, 이미 초기화된 디스크는 오류를 반환한다. RAW로 보이는 디스크에도 데이터가 있을 수 있으며 초기화하면 덮어쓴다. confirm=true가 필요하다. 대상은 가상 디스크 friendlyName 또는 uniqueId 중 하나로 지정한다. Windows가 아니면 오류를 반환한다.

- 힌트: 쓰기 · 파괴적
- 반환: `DiskInfo` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `friendlyName` | string |  |  | 가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |
| `partitionStyle` | string (`Raw`, `MBR`, `GPT`) |  | `GPT` | 파티션 스타일(PartitionStyle), MBR 또는 GPT, 기본 GPT |
| `confirm` | boolean |  | `false` | 초기화를 확인한다. true가 아니면 초기화하지 않고 오류를 반환한다 |

#### `create_partition`

로컬 Windows 저장소 공간(가상 디스크)의 초기화된 디스크 빈 공간에 파티션을 만든다(New-Partition). 기존 파티션은 바꾸지 않는다. 초기화되지 않은(RAW) 디스크는 오류를 반환한다. size 또는 useMaximumSize 중 하나를 지정한다. driveLetter를 지정하지 않으면 드라이브 문자를 자동 할당하며, assignDriveLetter=false면 할당하지 않는다. 대상은 가상 디스크 friendlyName 또는 uniqueId 중 하나로 지정한다. Windows가 아니면 오류를 반환한다.

- 힌트: 쓰기
- 반환: `PartitionInfo` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `friendlyName` | string |  |  | 가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |
| `size` | integer |  |  | 크기(바이트, Size), useMaximumSize와 함께 쓰지 않는다 |
| `useMaximumSize` | boolean |  | `false` | 가장 큰 빈 공간 전체 사용(UseMaximumSize), size와 함께 쓰지 않는다 |
| `offset` | integer |  |  | 시작 위치(바이트, Offset) |
| `alignment` | integer |  |  | 정렬 단위(바이트, Alignment) |
| `driveLetter` | string |  |  | 드라이브 문자(DriveLetter), A-Z 한 글자 |
| `assignDriveLetter` | boolean |  |  | 드라이브 문자 자동 할당(AssignDriveLetter), 생략하면 true, driveLetter와 false를 함께 쓰지 않는다 |
| `gptType` | string (`SystemPartition`, `MicrosoftReserved`, `BasicData`, `MicrosoftRecovery`) |  |  | GPT 파티션 유형(GptType), mbrType과 함께 쓰지 않는다 |
| `mbrType` | string (`FAT12`, `FAT16`, `Extended`, `Huge`, `IFS`, `FAT32`) |  |  | MBR 파티션 유형(MbrType), gptType과 함께 쓰지 않는다 |
| `isHidden` | boolean |  |  | 숨김 여부(IsHidden) |
| `isActive` | boolean |  |  | 활성 파티션 여부(IsActive, MBR 디스크) |

#### `format_volume`

로컬 Windows 저장소 공간(가상 디스크)의 파티션 1개에 있는 볼륨을 포맷한다(Format-Volume). 파일 시스템이 없는(RAW) 볼륨만 허용하며, 이미 포맷된 볼륨은 오류를 반환한다. 잠긴 BitLocker 볼륨, 인식되지 않는 파일 시스템처럼 RAW로 보이는 볼륨에도 데이터가 있을 수 있으며 포맷하면 사라진다. confirm=true가 필요하다. 대상은 가상 디스크 friendlyName 또는 uniqueId 중 하나와 partitionNumber로 지정한다. Windows가 아니면 오류를 반환한다.

- 힌트: 쓰기 · 파괴적
- 반환: `VolumeInfo` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `partitionNumber` | integer | ✓ |  | 파티션 번호(PartitionNumber), create_partition 결과의 partitionNumber |
| `friendlyName` | string |  |  | 가상 디스크 이름(FriendlyName), uniqueId와 함께 쓰지 않는다 |
| `uniqueId` | string |  |  | 가상 디스크 고유 ID(UniqueId), friendlyName과 함께 쓰지 않는다 |
| `fileSystem` | string (`FAT`, `FAT32`, `exFAT`, `NTFS`, `ReFS`) |  |  | 파일 시스템(FileSystem), 생략하면 시스템 기본값 |
| `newFileSystemLabel` | string |  |  | 볼륨 레이블(NewFileSystemLabel) |
| `allocationUnitSize` | integer |  |  | 할당 단위 크기(바이트, AllocationUnitSize) |
| `full` | boolean |  |  | 전체 포맷(Full), 생략하면 빠른 포맷 |
| `compress` | boolean |  |  | 압축 사용(Compress) |
| `shortFileNameSupport` | boolean |  |  | 8.3 짧은 파일 이름 지원(ShortFileNameSupport) |
| `setIntegrityStreams` | boolean |  |  | 무결성 스트림 사용(SetIntegrityStreams, ReFS) |
| `useLargeFRS` | boolean |  |  | 큰 파일 레코드 세그먼트 사용(UseLargeFRS, NTFS) |
| `disableHeatGathering` | boolean |  |  | 열 수집 비활성화(DisableHeatGathering, ReFS) |
| `isDAX` | boolean |  |  | DAX 모드(IsDAX) |
| `noTrim` | boolean |  |  | TRIM 생략(NoTrim) |
| `devDrive` | boolean |  |  | 개발 드라이브(DevDrive, ReFS) |
| `confirm` | boolean |  | `false` | 포맷을 확인한다. true가 아니면 포맷하지 않고 오류를 반환한다 |
| `timeoutSeconds` | integer |  | `300` | 저장소 job으로 진행될 때 완료를 기다리는 제한시간(초), 초과해도 job은 계속 진행된다 |

### AgentResource 문서

`RootPath` 아래 `rules`, `skills`, `tools`, `docs` 폴더의 문서를 다룬다. 경로가 해당 폴더 밖이면 오류를 반환한다.

#### rule 문서 — `Tools.AgentResource.RuleDocumentTools`

##### `list_rule_documents`

rules 아래 AGENTS.md 형식 rule 문서(.md) 목록을 front matter description과 함께 조회한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `RuleDocumentListResult` (structured content)

파라미터 없음

##### `get_rule_document`

rules 기준 상대 경로로 rule 문서(.md) 전문을 조회한다. 없거나 경로가 rules 밖이면 오류를 반환한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `RuleDocument` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | rules 기준 상대 경로(예: dotnet/naming.md, AGENTS.md), 확장자를 생략하면 .md |

##### `create_rule_document`

rules 아래에 rule 문서(.md)를 새로 만든다. content는 '---\ndescription: ...\n---' front matter를 포함한 전문이며 이미 있으면 오류를 반환한다.

- 힌트: 쓰기
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | rules 기준 상대 경로(예: dotnet/naming.md), 확장자를 생략하면 .md |
| `content` | string | ✓ |  | front matter를 포함한 문서 전문 |

##### `update_rule_document`

기존 rule 문서(.md) 전문을 content로 바꾼다. 없으면 오류를 반환한다.

- 힌트: 쓰기 · 멱등 · 파괴적
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | rules 기준 상대 경로(예: dotnet/naming.md), 확장자를 생략하면 .md |
| `content` | string | ✓ |  | front matter를 포함한 문서 전문 |

##### `delete_rule_document`

rule 문서(.md)를 삭제한다. 없으면 오류를 반환한다.

- 힌트: 쓰기 · 파괴적
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | rules 기준 상대 경로(예: dotnet/naming.md), 확장자를 생략하면 .md |

#### skill 문서 — `Tools.AgentResource.SkillDocumentTools`

##### `list_skill_documents`

skills 아래 skill 패키지(skills/<name>/SKILL.md) 목록을 front matter name·description과 함께 조회한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `SkillDocumentListResult` (structured content)

파라미터 없음

##### `get_skill_document`

skill 이름으로 SKILL.md 전문을 조회한다. 없거나 경로가 skills 밖이면 오류를 반환한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `SkillDocument` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | skills 아래 skill 폴더 이름(예: create-pr 또는 create-pr/SKILL.md) |

##### `create_skill_document`

skills/<name>/SKILL.md를 새로 만든다. content는 '---\nname: ...\ndescription: ...\n---' front matter를 포함한 전문이며 이미 있으면 오류를 반환한다.

- 힌트: 쓰기
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | skills 아래 skill 폴더 이름(예: create-pr) |
| `content` | string | ✓ |  | front matter를 포함한 SKILL.md 전문 |

##### `update_skill_document`

기존 skills/<name>/SKILL.md 전문을 content로 바꾼다. 없으면 오류를 반환한다.

- 힌트: 쓰기 · 멱등 · 파괴적
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | skills 아래 skill 폴더 이름(예: create-pr) |
| `content` | string | ✓ |  | front matter를 포함한 SKILL.md 전문 |

##### `delete_skill_document`

skills/<name>/SKILL.md를 삭제하고 폴더가 비면 폴더도 지운다. 없으면 오류를 반환한다.

- 힌트: 쓰기 · 파괴적
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | skills 아래 skill 폴더 이름(예: create-pr) |

#### tool 문서 — `Tools.AgentResource.ToolDocumentTools`

##### `list_tool_documents`

tools 아래 tool 레지스트리 문서(tools/<name>/TOOL.md) 목록을 description·language·location과 함께 조회한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `ToolDocumentListResult` (structured content)

파라미터 없음

##### `get_tool_document_usage`

등록된 tool의 위치(location)와 사용법(TOOL.md 본문)을 이름으로 조회한다. 없으면 오류를 반환한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `ToolDocumentUsage` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | tools 아래 tool 폴더 이름(예: xml-parser 또는 xml-parser/TOOL.md) |

##### `get_tool_document_registration_guide`

tool 문서 등록에 필요한 필드·저장 위치·예시를 조회한다. register_tool_document 호출 전에 사용한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `ToolRegistrationGuide` (structured content)

파라미터 없음

##### `register_tool_document`

이미 만든 도구의 메타데이터를 tools/<name>/TOOL.md로 등록한다. 도구 파일 자체는 만들거나 복사하지 않으며 이미 있으면 오류를 반환한다.

- 힌트: 쓰기
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | tools 아래 tool 폴더 이름(예: xml-parser) |
| `description` | string | ✓ |  | 목록에 보일 짧은 설명(언제 쓰는지) |
| `language` | string | ✓ |  | 개발 언어(예: C#, PowerShell, Python) |
| `location` | string | ✓ |  | 도구 파일 경로 또는 참조, 존재 여부는 확인하지 않는다 |
| `usage` | string | ✓ |  | 도구 사용법, TOOL.md 본문으로 저장한다 |

##### `update_tool_document`

등록된 tools/<name>/TOOL.md의 description·language·location·usage를 모두 바꾼다. 없으면 오류를 반환한다.

- 힌트: 쓰기 · 멱등 · 파괴적
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | tools 아래 tool 폴더 이름(예: xml-parser) |
| `description` | string | ✓ |  | 목록에 보일 짧은 설명(언제 쓰는지) |
| `language` | string | ✓ |  | 개발 언어(예: C#, PowerShell, Python) |
| `location` | string | ✓ |  | 도구 파일 경로 또는 참조, 존재 여부는 확인하지 않는다 |
| `usage` | string | ✓ |  | 도구 사용법, TOOL.md 본문으로 저장한다 |

##### `delete_tool_document`

tools/<name>/TOOL.md를 삭제하고 폴더가 비면 폴더도 지운다. location이 가리키는 도구 파일은 건드리지 않으며 없으면 오류를 반환한다.

- 힌트: 쓰기 · 파괴적
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `name` | string | ✓ |  | tools 아래 tool 폴더 이름(예: xml-parser) |

#### docs 문서 — `Tools.AgentResource.DocDocumentTools`

##### `list_doc_documents`

docs/{category}/NNNN-{feature}-{phase}.md 문서 목록을 front matter status·feature·phase와 함께 조회한다. category·feature로 거를 수 있다(대소문자 무시).

- 힌트: 읽기 전용 · 멱등
- 반환: `DocDocumentListResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `category` | string |  |  | 카테고리 필터(폴더 이름), 생략하면 전체 |
| `feature` | string |  |  | 기능 필터(front matter feature), 생략하면 전체 |

##### `get_doc_document`

category와 4자리 순번으로 docs 문서 전문을 조회한다. 없거나 같은 번호 문서가 여럿이면 오류를 반환한다.

- 힌트: 읽기 전용 · 멱등
- 반환: `DocDocument` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `category` | string | ✓ |  | 카테고리(소문자 영문·숫자·하이픈, 예: agent-resource) |
| `number` | integer | ✓ |  | 카테고리 안 순번(1~9999) |

##### `create_doc_document`

docs/{category}/에 다음 4자리 순번으로 NNNN-{feature}-{phase}.md를 만든다. front matter(status·category·feature·phase)는 Tool이 만들고 content는 본문만 받는다.

- 힌트: 쓰기
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `category` | string | ✓ |  | 카테고리(소문자 영문·숫자·하이픈, 예: agent-resource) |
| `feature` | string | ✓ |  | 기능 식별자(공백·/ 없음, 예: storage-pool) |
| `phase` | string | ✓ |  | 단계(공백·/·- 없음, 예: 방안, 설계, 개발) |
| `content` | string | ✓ |  | front matter를 뺀 본문(Markdown) |
| `status` | string |  | `"draft"` | front matter status, 기본 draft |

##### `update_doc_document`

category·순번으로 찾은 docs 문서의 본문을 content로 바꾼다. feature·phase는 유지하고 status는 생략하면 유지한다. front matter의 다른 키는 남지 않는다.

- 힌트: 쓰기 · 멱등 · 파괴적
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `category` | string | ✓ |  | 카테고리(소문자 영문·숫자·하이픈, 예: agent-resource) |
| `number` | integer | ✓ |  | 카테고리 안 순번(1~9999) |
| `content` | string | ✓ |  | front matter를 뺀 새 본문(Markdown) |
| `status` | string |  |  | 새 front matter status(예: done), 생략하면 유지 |

##### `delete_doc_document`

category·순번으로 찾은 docs 문서를 삭제한다. 없거나 같은 번호 문서가 여럿이면 오류를 반환한다.

- 힌트: 쓰기 · 파괴적
- 반환: `AgentResourceWriteResult` (structured content)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `category` | string | ✓ |  | 카테고리(소문자 영문·숫자·하이픈, 예: agent-resource) |
| `number` | integer | ✓ |  | 카테고리 안 순번(1~9999) |

### Redmine

`Version`에 따라 등록되는 클래스가 다르다. 같은 이름의 Tool이라도 V2_2와 V7_0의 파라미터가 다를 수 있으므로 해당 버전 절을 본다.

| Tool 클래스 | V2_2 | V7_0 |
| --- | --- | --- |
| `Tools.Redmine.WikiTools`, `ProjectTools`, `UserTools`, `MetadataTools`, `AttachmentTools` | ✓ | ✓ |
| `Tools.Redmine.IssueTools`, `RelationTools`, `TimeEntryTools`, `TextGuideTools` | ✓ | |
| `Tools.Redmine.V7.IssueToolsV7`, `RelationToolsV7`, `TimeEntryToolsV7`, `TextGuideToolsV7` | | ✓ |
| `Tools.Redmine.V7.JournalTools`, `WikiExtraTools`, `VersionTools`, `IssueCategoryTools`, `MembershipTools`, `SearchTools`, `PrincipalTools`, `CustomFieldTools`, `ProjectContentTools`, `AttachmentManageTools` | | ✓ |

#### 공통(V2_2·V7_0)

##### 위키 — `Tools.Redmine.WikiTools`

###### `list_wiki_pages`

프로젝트의 Redmine 위키 페이지 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |

###### `get_wiki_page`

Redmine 위키 페이지 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `title` | string | ✓ |  | 위키 페이지 제목 |

###### `put_wiki_page`

Redmine 위키 페이지를 생성하거나 수정한다. 기존 페이지를 고치면 성공 본문이 비어 있다. 새 페이지를 만들 때만 페이지 JSON이 온다. 첨부만 붙일 때는 get_wiki_page의 현재 text를 그대로 보낸다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `title` | string | ✓ |  | 위키 페이지 제목 |
| `text` | string | ✓ |  | 본문 |
| `comments` | string |  |  | 변경 설명 |
| `uploads` | UploadReference[] |  |  | upload_file로 받은 토큰 목록. 넘기면 위키 페이지에 첨부된다. |

##### 프로젝트 — `Tools.Redmine.ProjectTools`

###### `list_projects`

Redmine 프로젝트 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

###### `get_project`

Redmine 프로젝트 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | string | ✓ |  | 프로젝트 ID 또는 식별자 |

##### 사용자 — `Tools.Redmine.UserTools`

###### `get_current_user`

일반 API 키로 현재 Redmine 사용자를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

###### `list_users`

Redmine 사용자 목록을 조회한다. 관리자 권한이 필요하다. 활성 사용자만 반환한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

###### `get_user`

Redmine 사용자 하나를 조회한다. 관리자가 아니면 활성 사용자가 아니거나, 본인이 아니면서 보이는 멤버십과 활동이 없으면 404이다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 사용자 ID |

##### 메타데이터 — `Tools.Redmine.MetadataTools`

###### `list_issue_statuses`

Redmine 이슈 상태 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

###### `list_trackers`

Redmine 트래커 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

###### `list_issue_priorities`

Redmine 이슈 우선순위 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

###### `list_time_entry_activities`

Redmine 시간 기록 활동 목록을 조회한다. 시스템 공통 활동만 반환한다. 프로젝트에서 재정의한 활동 ID는 목록에 없다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

##### 첨부 — `Tools.Redmine.AttachmentTools`

###### `download_attachment`

Redmine 첨부파일 본문을 다운로드한다. 응답 JSON에 text 또는 contentBase64를 넣으며, 5MiB를 넘으면 outputPath가 필요하다. outputPath는 MCP 서버 호스트 경로이다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `attachmentId` | integer |  |  | 첨부 ID. issueId+filename과 둘 중 하나가 필요하다. 둘 다 있으면 attachmentId를 우선한다. |
| `issueId` | integer |  |  | 일감 ID. attachmentId가 없을 때 filename과 함께 첨부를 찾는다. |
| `filename` | string |  |  | 첨부 파일명. issueId와 함께 사용한다. 대소문자를 구분한다. |
| `contentUrl` | string |  |  | 본문 다운로드 URL. 있으면 메타 content_url 대신 사용한다. Redmine API endpoint와 같은 origin만 허용한다. |
| `outputPath` | string |  |  | MCP 서버 호스트에 저장할 경로. 5MiB 초과 시 필수이다. |

###### `upload_file`

파일을 Redmine에 업로드하고 토큰을 반환한다. 토큰을 create_issue, update_issue, create_subtask, put_wiki_page의 uploads로 넘겨야 첨부가 된다. content, contentBase64, sourcePath 중 하나만 지정한다. content와 contentBase64는 5MiB까지이고, 더 크면 sourcePath를 쓴다. sourcePath는 MCP 서버 호스트 경로이다. 요청 제한 시간은 100초이다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `content` | string |  |  | UTF-8 텍스트 본문 |
| `contentBase64` | string |  |  | base64로 인코딩한 본문 |
| `sourcePath` | string |  |  | MCP 서버 호스트의 파일 경로 |

#### V2_2 전용

##### 일감 — `Tools.Redmine.IssueTools`

###### `list_issues`

Redmine 이슈 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string |  |  | 프로젝트 ID 또는 식별자 |
| `statusId` | string |  |  | 상태 ID, open, closed, *. 비우면 열린 일감만 반환한다. 전체는 *이다. |
| `assignedToId` | string |  |  | 담당자 ID 또는 me |
| `trackerId` | integer |  |  | 트래커 ID |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

###### `get_issue`

Redmine 이슈 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 이슈 ID |
| `include` | string |  | `"journals"` | 함께 받을 연관 데이터. 기본값은 journals이다. children, attachments, relations, changesets를 받을 수 있다. watchers는 2.2 응답에 없다. |

###### `create_issue`

Redmine 이슈를 생성한다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `subject` | string | ✓ |  | 제목 |
| `description` | string |  |  | 설명 |
| `trackerId` | integer |  |  | 트래커 ID |
| `statusId` | integer |  |  | 상태 ID |
| `priorityId` | integer |  |  | 우선순위 ID |
| `assignedToId` | integer |  |  | 담당자 ID |
| `parentIssueId` | integer |  |  | 부모 이슈 ID |
| `startDate` | string |  |  | 시작일. yyyy-MM-dd |
| `dueDate` | string |  |  | 완료 기한. yyyy-MM-dd |
| `estimatedHours` | number |  |  | 예상 시간 |
| `doneRatio` | integer |  |  | 진척도. 0부터 100 |
| `uploads` | UploadReference[] |  |  | upload_file로 받은 토큰 목록. 넘기면 일감에 첨부된다. |

###### `create_subtask`

부모 일감 아래에 하위 일감을 생성한다. projectId를 비우면 부모 일감의 프로젝트를 쓴다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `parentIssueId` | integer | ✓ |  | 부모 일감 ID |
| `subject` | string | ✓ |  | 제목 |
| `projectId` | string |  |  | 프로젝트 ID 또는 식별자. 비우면 부모 일감의 프로젝트 |
| `description` | string |  |  | 설명 |
| `trackerId` | integer |  |  | 트래커 ID |
| `statusId` | integer |  |  | 상태 ID |
| `priorityId` | integer |  |  | 우선순위 ID |
| `assignedToId` | integer |  |  | 담당자 ID |
| `startDate` | string |  |  | 시작일. yyyy-MM-dd |
| `dueDate` | string |  |  | 완료 기한. yyyy-MM-dd |
| `estimatedHours` | number |  |  | 예상 시간 |
| `doneRatio` | integer |  |  | 진척도. 0부터 100 |
| `uploads` | UploadReference[] |  |  | upload_file로 받은 토큰 목록. 넘기면 일감에 첨부된다. |

###### `update_issue`

Redmine 이슈를 수정하거나 코멘트를 추가한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 이슈 ID |
| `subject` | string |  |  | 제목 |
| `description` | string |  |  | 설명 |
| `trackerId` | integer |  |  | 트래커 ID |
| `statusId` | integer |  |  | 상태 ID |
| `priorityId` | integer |  |  | 우선순위 ID |
| `assignedToId` | integer |  |  | 담당자 ID |
| `parentIssueId` | integer |  |  | 부모 이슈 ID |
| `startDate` | string |  |  | 시작일. yyyy-MM-dd |
| `dueDate` | string |  |  | 완료 기한. yyyy-MM-dd |
| `estimatedHours` | number |  |  | 예상 시간 |
| `doneRatio` | integer |  |  | 진척도. 0부터 100 |
| `notes` | string |  |  | 코멘트 |
| `uploads` | UploadReference[] |  |  | upload_file로 받은 토큰 목록. 넘기면 일감에 첨부된다. |

##### 일감 관계 — `Tools.Redmine.RelationTools`

###### `list_issue_relations`

Redmine 일감의 연결된 일감(관계) 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `issueId` | integer | ✓ |  | 일감 ID |

###### `create_issue_relation`

두 Redmine 일감을 연결된 일감(관계)으로 등록한다. 관계는 수정할 수 없으므로 바꾸려면 삭제 후 다시 만든다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `issueId` | integer | ✓ |  | 기준 일감 ID |
| `issueToId` | integer | ✓ |  | 연결할 대상 일감 ID |
| `relationType` | string | ✓ |  | 관계 종류. relates, duplicates, duplicated, blocks, blocked, precedes, follows 중 하나이다. copied_to는 2.2에 없다. |
| `delay` | integer |  |  | 지연 일수. precedes, follows일 때만 쓸 수 있다. |

###### `delete_issue_relation`

Redmine 일감 관계를 삭제한다. 관계 ID는 list_issue_relations 또는 get_issue(include=relations)로 확인한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `relationId` | integer | ✓ |  | 관계 ID |

##### 작업 시간 — `Tools.Redmine.TimeEntryTools`

###### `list_time_entries`

Redmine 시간 기록 목록을 조회한다. 사용자로는 거르지 못한다. issueId가 있으면 projectId는 적용되지 않는다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string |  |  | 프로젝트 ID 또는 식별자. issueId가 있으면 적용되지 않는다. |
| `issueId` | integer |  |  | 이슈 ID. 있으면 이 일감의 기록만 조회한다. |
| `from` | string |  |  | 시작일. yyyy-MM-dd |
| `to` | string |  |  | 종료일. yyyy-MM-dd |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

###### `get_time_entry`

Redmine 시간 기록 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 시간 기록 ID |

###### `create_time_entry`

Redmine 시간 기록을 생성한다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `hours` | number | ✓ |  | 시간 |
| `issueId` | integer |  |  | 이슈 ID. projectId가 없으면 필수 |
| `projectId` | integer |  |  | 프로젝트 ID. issueId가 없으면 필수 |
| `activityId` | integer |  |  | 활동 ID |
| `comments` | string |  |  | 설명 |
| `spentOn` | string |  |  | 작업일. yyyy-MM-dd |

###### `update_time_entry`

Redmine 시간 기록을 수정한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 시간 기록 ID |
| `hours` | number |  |  | 시간 |
| `issueId` | integer |  |  | 이슈 ID |
| `projectId` | integer |  |  | 프로젝트 ID |
| `activityId` | integer |  |  | 활동 ID |
| `comments` | string |  |  | 설명 |
| `spentOn` | string |  |  | 작업일. yyyy-MM-dd |

##### 텍스트 가이드 — `Tools.Redmine.TextGuideTools`

###### `get_text_guide`

Redmine 2.2 Textile 예약어와 일감, 댓글, 위키 본문 템플릿을 반환한다. 설명, 댓글, 위키 본문을 쓰기 전에 호출한다.

- 힌트: 읽기 전용 · 멱등
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `target` | string | ✓ |  | issue, comment, wiki 중 하나 |

#### V7_0 전용

##### 일감 — `Tools.Redmine.V7.IssueToolsV7`

###### `list_issues`

Redmine 이슈 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string |  |  | 프로젝트 ID 또는 식별자 |
| `statusId` | string |  |  | 상태 ID, open, closed, *. 비우면 열린 일감만 반환한다. 전체는 *이다. |
| `assignedToId` | string |  |  | 담당자 ID 또는 me |
| `trackerId` | integer |  |  | 트래커 ID |
| `subprojectId` | string |  |  | 하위 프로젝트 ID 또는 식별자. !*이면 하위 프로젝트 일감을 뺀다. |
| `parentId` | integer |  |  | 부모 일감 ID |
| `issueIds` | string |  |  | 일감 ID 쉼표 목록. 예: 1,2,3 |
| `authorId` | string |  |  | 작성자 ID 또는 me |
| `categoryId` | integer |  |  | 일감 범주 ID |
| `fixedVersionId` | string |  |  | 대상 버전 ID |
| `createdOn` | string |  |  | 생성일 조건. 예: >=2026-01-01, <=2026-01-31, ><2026-01-01\|2026-01-31 |
| `updatedOn` | string |  |  | 수정일 조건. createdOn과 같은 형식 |
| `queryId` | integer |  |  | 저장된 질의 ID. list_queries로 찾는다. |
| `sort` | string |  |  | 정렬. 예: updated_on:desc,id |
| `include` | string |  |  | 함께 받을 연관 데이터. attachments, relations 쉼표 목록 |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

###### `get_issue`

Redmine 이슈 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 이슈 ID |
| `include` | string |  | `"journals"` | 함께 받을 연관 데이터. 기본값은 journals이다. children, attachments, relations, changesets, journals, watchers, allowed_statuses를 쉼표로 받을 수 있다. |

###### `create_issue`

Redmine 이슈를 생성한다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `subject` | string | ✓ |  | 제목 |
| `description` | string |  |  | 설명 |
| `trackerId` | integer |  |  | 트래커 ID |
| `statusId` | integer |  |  | 상태 ID |
| `priorityId` | integer |  |  | 우선순위 ID |
| `assignedToId` | integer |  |  | 담당자 ID |
| `parentIssueId` | integer |  |  | 부모 이슈 ID |
| `startDate` | string |  |  | 시작일. yyyy-MM-dd |
| `dueDate` | string |  |  | 완료 기한. yyyy-MM-dd |
| `estimatedHours` | number |  |  | 예상 시간 |
| `doneRatio` | integer |  |  | 진척도. 0부터 100 |
| `uploads` | UploadReference[] |  |  | upload_file로 받은 토큰 목록. 넘기면 일감에 첨부된다. |
| `isPrivate` | boolean |  |  | 비공개 일감 여부 |
| `categoryId` | integer |  |  | 일감 범주 ID |
| `fixedVersionId` | integer |  |  | 대상 버전 ID |
| `customFields` | CustomFieldValue[] |  |  | 사용자 정의 필드 값 목록. 항목마다 value(단일)와 values(다중) 중 하나만 넣는다. |
| `watcherUserIds` | integer[] |  |  | 관찰자 사용자 ID 목록 |

###### `create_subtask`

부모 일감 아래에 하위 일감을 생성한다. projectId를 비우면 부모 일감의 프로젝트를 쓴다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `parentIssueId` | integer | ✓ |  | 부모 일감 ID |
| `subject` | string | ✓ |  | 제목 |
| `projectId` | string |  |  | 프로젝트 ID 또는 식별자. 비우면 부모 일감의 프로젝트 |
| `description` | string |  |  | 설명 |
| `trackerId` | integer |  |  | 트래커 ID |
| `statusId` | integer |  |  | 상태 ID |
| `priorityId` | integer |  |  | 우선순위 ID |
| `assignedToId` | integer |  |  | 담당자 ID |
| `startDate` | string |  |  | 시작일. yyyy-MM-dd |
| `dueDate` | string |  |  | 완료 기한. yyyy-MM-dd |
| `estimatedHours` | number |  |  | 예상 시간 |
| `doneRatio` | integer |  |  | 진척도. 0부터 100 |
| `uploads` | UploadReference[] |  |  | upload_file로 받은 토큰 목록. 넘기면 일감에 첨부된다. |
| `isPrivate` | boolean |  |  | 비공개 일감 여부 |
| `categoryId` | integer |  |  | 일감 범주 ID |
| `fixedVersionId` | integer |  |  | 대상 버전 ID |
| `customFields` | CustomFieldValue[] |  |  | 사용자 정의 필드 값 목록. 항목마다 value(단일)와 values(다중) 중 하나만 넣는다. |
| `watcherUserIds` | integer[] |  |  | 관찰자 사용자 ID 목록 |

###### `update_issue`

Redmine 이슈를 수정하거나 코멘트를 추가한다. 기존 코멘트는 update_journal로 고친다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 이슈 ID |
| `subject` | string |  |  | 제목 |
| `description` | string |  |  | 설명 |
| `trackerId` | integer |  |  | 트래커 ID |
| `statusId` | integer |  |  | 상태 ID |
| `priorityId` | integer |  |  | 우선순위 ID |
| `assignedToId` | integer |  |  | 담당자 ID |
| `parentIssueId` | integer |  |  | 부모 이슈 ID |
| `startDate` | string |  |  | 시작일. yyyy-MM-dd |
| `dueDate` | string |  |  | 완료 기한. yyyy-MM-dd |
| `estimatedHours` | number |  |  | 예상 시간 |
| `doneRatio` | integer |  |  | 진척도. 0부터 100 |
| `notes` | string |  |  | 코멘트 |
| `uploads` | UploadReference[] |  |  | upload_file로 받은 토큰 목록. 넘기면 일감에 첨부된다. |
| `isPrivate` | boolean |  |  | 비공개 일감 여부 |
| `categoryId` | integer |  |  | 일감 범주 ID |
| `fixedVersionId` | integer |  |  | 대상 버전 ID |
| `customFields` | CustomFieldValue[] |  |  | 사용자 정의 필드 값 목록. 항목마다 value(단일)와 values(다중) 중 하나만 넣는다. |
| `privateNotes` | boolean |  |  | notes를 비공개 코멘트로 남길지 여부 |

###### `delete_issue`

Redmine 이슈를 삭제한다. 되돌릴 수 없다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 이슈 ID |

###### `add_issue_watcher`

Redmine 이슈에 관찰자를 추가한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `issueId` | integer | ✓ |  | 이슈 ID |
| `userId` | integer | ✓ |  | 관찰자로 추가할 사용자 ID |

###### `remove_issue_watcher`

Redmine 이슈에서 관찰자를 뺀다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `issueId` | integer | ✓ |  | 이슈 ID |
| `userId` | integer | ✓ |  | 뺄 사용자 ID |

##### 일감 관계 — `Tools.Redmine.V7.RelationToolsV7`

###### `list_issue_relations`

Redmine 일감의 연결된 일감(관계) 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `issueId` | integer | ✓ |  | 일감 ID |

###### `create_issue_relation`

두 Redmine 일감을 연결된 일감(관계)으로 등록한다. 관계는 수정할 수 없으므로 바꾸려면 삭제 후 다시 만든다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `issueId` | integer | ✓ |  | 기준 일감 ID |
| `issueToId` | integer | ✓ |  | 연결할 대상 일감 ID |
| `relationType` | string | ✓ |  | 관계 종류. relates, duplicates, duplicated, blocks, blocked, precedes, follows, copied_to, copied_from 중 하나이다. |
| `delay` | integer |  |  | 지연 일수. precedes, follows일 때만 쓸 수 있다. |

###### `delete_issue_relation`

Redmine 일감 관계를 삭제한다. 관계 ID는 list_issue_relations 또는 get_issue(include=relations)로 확인한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `relationId` | integer | ✓ |  | 관계 ID |

##### 작업 시간 — `Tools.Redmine.V7.TimeEntryToolsV7`

###### `list_time_entries`

Redmine 시간 기록 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string |  |  | 프로젝트 ID 또는 식별자 |
| `issueId` | integer |  |  | 이슈 ID. 있으면 이 일감의 기록만 조회한다. |
| `userId` | string |  |  | 사용자 ID 또는 me |
| `from` | string |  |  | 시작일. yyyy-MM-dd |
| `to` | string |  |  | 종료일. yyyy-MM-dd |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

###### `get_time_entry`

Redmine 시간 기록 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 시간 기록 ID |

###### `create_time_entry`

Redmine 시간 기록을 생성한다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `hours` | number | ✓ |  | 시간 |
| `issueId` | integer |  |  | 이슈 ID. projectId가 없으면 필수 |
| `projectId` | integer |  |  | 프로젝트 ID. issueId가 없으면 필수 |
| `activityId` | integer |  |  | 활동 ID |
| `comments` | string |  |  | 설명 |
| `spentOn` | string |  |  | 작업일. yyyy-MM-dd |
| `userId` | integer |  |  | 대신 기록할 사용자 ID. 다른 사용자 시간 기록 권한이 필요하다. 비우면 API 키 사용자다. |

###### `update_time_entry`

Redmine 시간 기록을 수정한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 시간 기록 ID |
| `hours` | number |  |  | 시간 |
| `issueId` | integer |  |  | 이슈 ID |
| `projectId` | integer |  |  | 프로젝트 ID |
| `activityId` | integer |  |  | 활동 ID |
| `comments` | string |  |  | 설명 |
| `spentOn` | string |  |  | 작업일. yyyy-MM-dd |

###### `delete_time_entry`

Redmine 시간 기록을 삭제한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 시간 기록 ID |

##### 텍스트 가이드 — `Tools.Redmine.V7.TextGuideToolsV7`

###### `get_text_guide`

이 Redmine 서버의 텍스트 서식(Textile 또는 CommonMark)에 맞는 문법과 일감, 댓글, 위키 본문 템플릿을 반환한다. 설명, 댓글, 위키 본문을 쓰기 전에 호출한다.

- 힌트: 읽기 전용 · 멱등
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `target` | string | ✓ |  | issue, comment, wiki 중 하나 |

##### 일감 댓글 — `Tools.Redmine.V7.JournalTools`

###### `update_journal`

Redmine 일감 댓글(journal)을 수정한다. 댓글 ID는 get_issue(include=journals)의 journals[].id이다. 본인 댓글은 edit_own_issue_notes, 남의 댓글은 edit_issue_notes 권한이 필요하다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 댓글(journal) ID |
| `notes` | string |  |  | 새 댓글 본문. 빈 문자열도 그대로 보낸다. |
| `privateNotes` | boolean |  |  | 비공개 댓글 여부 |

##### 위키 추가 — `Tools.Redmine.V7.WikiExtraTools`

###### `get_wiki_page_version`

Redmine 위키 페이지의 특정 버전(이전 내용)을 조회한다. 현재 버전 번호는 get_wiki_page의 version이다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `title` | string | ✓ |  | 위키 페이지 제목 |
| `version` | integer | ✓ |  | 버전 번호. 1부터 |

###### `delete_wiki_page`

Redmine 위키 페이지를 삭제한다. 하위 페이지는 삭제되지 않고 최상위 페이지가 된다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `title` | string | ✓ |  | 위키 페이지 제목 |

##### 버전 — `Tools.Redmine.V7.VersionTools`

###### `list_versions`

프로젝트의 Redmine 버전 목록을 조회한다. 다른 프로젝트에서 공유된 버전도 포함된다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |

###### `get_version`

Redmine 버전 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 버전 ID |

###### `create_version`

프로젝트에 Redmine 버전을 생성한다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `name` | string | ✓ |  | 버전 이름 |
| `status` | string |  |  | 상태. open, locked, closed 중 하나 |
| `sharing` | string |  |  | 공유 범위. none, descendants, hierarchy, tree, system 중 하나 |
| `dueDate` | string |  |  | 완료 기한. yyyy-MM-dd |
| `description` | string |  |  | 설명 |
| `wikiPageTitle` | string |  |  | 연결할 위키 페이지 제목 |

###### `update_version`

Redmine 버전을 수정한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 버전 ID |
| `name` | string |  |  | 버전 이름 |
| `status` | string |  |  | 상태. open, locked, closed 중 하나 |
| `sharing` | string |  |  | 공유 범위. none, descendants, hierarchy, tree, system 중 하나 |
| `dueDate` | string |  |  | 완료 기한. yyyy-MM-dd |
| `description` | string |  |  | 설명 |
| `wikiPageTitle` | string |  |  | 연결할 위키 페이지 제목 |

###### `delete_version`

Redmine 버전을 삭제한다. 일감이 연결된 버전은 서버가 거부한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 버전 ID |

##### 일감 범주 — `Tools.Redmine.V7.IssueCategoryTools`

###### `list_issue_categories`

프로젝트의 Redmine 일감 범주 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |

###### `get_issue_category`

Redmine 일감 범주 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 일감 범주 ID |

###### `create_issue_category`

프로젝트에 Redmine 일감 범주를 생성한다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `name` | string | ✓ |  | 범주 이름 |
| `assignedToId` | integer |  |  | 이 범주 일감의 기본 담당자 ID |

###### `update_issue_category`

Redmine 일감 범주를 수정한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 일감 범주 ID |
| `name` | string |  |  | 범주 이름 |
| `assignedToId` | integer |  |  | 이 범주 일감의 기본 담당자 ID |

###### `delete_issue_category`

Redmine 일감 범주를 삭제한다. reassignToId를 주면 이 범주의 일감을 그 범주로 옮기고, 비우면 일감의 범주를 비운다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 일감 범주 ID |
| `reassignToId` | integer |  |  | 일감을 옮길 범주 ID |

##### 구성원 — `Tools.Redmine.V7.MembershipTools`

###### `list_memberships`

Redmine 프로젝트 구성원(사용자·그룹과 역할) 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

###### `get_membership`

Redmine 프로젝트 구성원 하나를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 구성원 ID |

###### `create_membership`

Redmine 프로젝트에 사용자나 그룹을 구성원으로 추가한다. 역할 ID는 list_roles로 찾는다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |
| `userId` | integer | ✓ |  | 사용자 ID 또는 그룹 ID |
| `roleIds` | integer[] | ✓ |  | 역할 ID 목록. 하나 이상 |

###### `update_membership`

Redmine 프로젝트 구성원의 역할을 바꾼다. roleIds로 역할 전체를 교체한다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 구성원 ID |
| `roleIds` | integer[] | ✓ |  | 새 역할 ID 목록. 하나 이상 |

###### `delete_membership`

Redmine 프로젝트에서 구성원을 제거한다. 그룹을 통해 상속된 구성원은 제거할 수 없다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 구성원 ID |

##### 검색 — `Tools.Redmine.V7.SearchTools`

###### `search`

Redmine 전체 또는 프로젝트 안에서 검색한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `query` | string | ✓ |  | 검색어 |
| `projectId` | string |  |  | 프로젝트 ID 또는 식별자. 비우면 전체 |
| `scope` | string |  |  | 범위. all, my_projects, subprojects 중 하나 |
| `allWords` | boolean |  |  | 모든 단어 포함 여부. 기본 true. false면 한 단어라도 포함 |
| `titlesOnly` | boolean |  |  | 제목만 검색할지 여부. 기본 false |
| `types` | string |  |  | 검색 대상 쉼표 목록. issues, news, documents, changesets, wiki_pages, messages, projects. 비우면 전체 |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

###### `list_queries`

Redmine에 저장된 일감 질의(공개 질의와 내 질의) 목록을 조회한다. id는 list_issues의 queryId로 쓴다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

##### 역할·그룹·내 계정 — `Tools.Redmine.V7.PrincipalTools`

###### `list_roles`

Redmine 역할 목록을 조회한다. id는 구성원 추가·수정의 roleIds로 쓴다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

###### `get_role`

Redmine 역할 하나와 그 권한 목록을 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 역할 ID |

###### `list_groups`

Redmine 그룹 목록을 조회한다. 관리자 권한이 필요하다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

###### `get_group`

Redmine 그룹 하나를 조회한다. 관리자 권한이 필요하다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 그룹 ID |

###### `get_my_account`

API 키 사용자의 계정 정보(로그인, 메일, 사용자 정의 필드 등)를 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

##### 사용자 정의 필드 — `Tools.Redmine.V7.CustomFieldTools`

###### `list_custom_fields`

Redmine 사용자 정의 필드 정의 목록(형식, 가능한 값, 필수 여부)을 조회한다. 관리자 권한이 필요하다. id는 일감 생성·수정의 customFields에 쓴다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

파라미터 없음

##### 프로젝트 파일·뉴스 — `Tools.Redmine.V7.ProjectContentTools`

###### `list_project_files`

Redmine 프로젝트의 파일 목록을 조회한다. 내려받기는 download_attachment에 content_url을 넘긴다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string | ✓ |  | 프로젝트 ID 또는 식별자 |

###### `list_news`

Redmine 뉴스 목록을 조회한다. projectId를 비우면 볼 수 있는 모든 프로젝트의 뉴스다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `projectId` | string |  |  | 프로젝트 ID 또는 식별자. 비우면 전체 |
| `limit` | integer |  |  | 페이지 크기. 기본 25, 최대 100 |
| `offset` | integer |  |  | 건너뛸 개수. 기본 0 |

##### 첨부 관리 — `Tools.Redmine.V7.AttachmentManageTools`

###### `update_attachment`

Redmine 첨부의 파일명이나 설명을 바꾼다. 파일 내용은 바꿀 수 없다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 첨부 ID |
| `filename` | string |  |  | 새 파일명 |
| `description` | string |  |  | 새 설명 |

###### `delete_attachment`

Redmine 첨부를 삭제한다. 되돌릴 수 없다. 성공 시 본문이 비어 있다. 이 도구는 Redmine에 반영한다. 호출 전에 변경 내용을 사용자에게 보여주고 동의를 받은 뒤에만 호출한다.

- 힌트: 쓰기 · 멱등 · 파괴적 · 외부 접근
- 반환: 문자열

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `id` | integer | ✓ |  | 첨부 ID |

### Jenkins MCP 중계 — `Tools.Jenkins.JenkinsTools`

#### `jenkins_list_instances`

등록한 Jenkins 인스턴스의 이름, 용도 설명, MCP Endpoint를 조회한다. User·ApiToken은 포함하지 않는다.

- 힌트: 읽기 전용 · 멱등
- 반환: 문자열(`[{name, description, endpoint}]`)

#### `jenkins_list_tools`

지정한 Jenkins 인스턴스의 MCP Tool 목록을 조회한다. 결과는 인스턴스별로 캐시되며 `refresh`가 true일 때만 다시 조회한다.

- 힌트: 읽기 전용 · 멱등 · 외부 접근
- 반환: 문자열(`{instance, tools: [{name, title, description, inputSchema, annotations}]}`)

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `instance` | string | ✓ |  | Jenkins 인스턴스 이름. 생략하면 등록 이름을 안내하는 오류 결과 |
| `refresh` | boolean |  | false | true면 캐시를 무시하고 다시 조회 |

#### `jenkins_call_tool`

지정한 Jenkins 인스턴스의 MCP Tool을 호출하고 Jenkins가 돌려준 결과(content, structuredContent, isError)를 그대로 반환한다.

- 힌트: 쓰기 · 파괴적 · 외부 접근
- 반환: Jenkins Tool 결과 그대로

| 파라미터 | 타입 | 필수 | 기본값 | 설명 |
| --- | --- | --- | --- | --- |
| `instance` | string | ✓ |  | Jenkins 인스턴스 이름 |
| `tool` | string | ✓ |  | 호출할 Jenkins MCP Tool 이름(`jenkins_list_tools`로 확인) |
| `arguments` | object 또는 string |  |  | Tool 인자. JSON 객체 또는 JSON 객체 문자열 |

`instance`, `tool`은 필수지만 생략한 호출에도 안내 메시지를 주려고 입력 스키마의 `required`에는 넣지 않았다.

오류(모두 JSON-RPC 오류가 아닌 `isError: true` 결과로 반환):

- `instance` 누락·미등록, `tool` 누락·공백, `arguments`가 객체가 아니면 Jenkins를 호출하지 않는다. `instance` 문제면 등록된 이름을 안내한다.
- Jenkins가 보낸 MCP 오류(없는 Tool, 잘못된 인자 등)는 `Jenkins '{instance}' returned error {code}: ...`로 반환한다. 연결은 유지한다.
- 연결·HTTP 오류는 `Jenkins '{instance}' request failed (HTTP 401): ...`로 반환하고 해당 인스턴스 연결을 버린다. 다음 호출에서 다시 연결한다.

### GitHub MCP 중계 — `Tools.GitHub.GitHubTools`

`github_list_instances`, `github_list_tools`, `github_call_tool`은 Jenkins 중계 Tool과 파라미터·힌트·반환·캐시가 같다. 차이는 다음과 같다.

- `github_list_instances`는 Token을 포함하지 않는다.
- 오류 메시지의 서비스 이름이 `GitHub`이다: `Unknown GitHub instance '{name}'`, `GitHub '{instance}' returned error {code}: ...`, `GitHub '{instance}' request failed (HTTP 401): ...`
- 원격 GitHub MCP는 Tool이 많아 `github_list_tools` 결과가 클 수 있다.

### GitHub 태그·ref (REST) — `Tools.GitHub.GitHubRefTools`

GitHub MCP Server가 노출하지 않는 태그·ref 쓰기를 `ApiEndpoint`의 GitHub REST API로 직접 처리한다. 모두 `instance`, `owner`, `repo`가 필수이고 Contents 쓰기 권한이 필요하다(권한 검사는 GitHub가 한다).

| Tool | 추가 파라미터 | REST | 힌트 | 반환 |
| --- | --- | --- | --- | --- |
| `github_create_lightweight_tag` | `tag`, `sha` | `POST git/refs` | 쓰기 · 외부 접근 | GitHub ref 응답 그대로 |
| `github_create_annotated_tag` | `tag`, `sha`(커밋), `message`, `taggerName`·`taggerEmail`(선택, 함께) | `POST git/tags` → `POST git/refs` | 쓰기 · 외부 접근 | `{tag, ref}` 두 응답 |
| `github_update_ref` | `ref`, `sha`, `force`(기본 false) | `PATCH git/refs/{ref}` | 쓰기 · 파괴적 · 멱등 · 외부 접근 | GitHub ref 응답 그대로 |
| `github_delete_ref` | `ref` | `DELETE git/refs/{ref}` | 쓰기 · 파괴적 · 멱등 · 외부 접근 | `{"deleted":"refs/..."}` |

- `ref`는 `tags/v1.0.0`, `heads/main` 형식이며 앞의 `refs/`는 있어도 된다. 태그 삭제도 `github_delete_ref`(`tags/{tag}`)로 한다. 주석 태그의 태그 객체는 삭제 API가 없어 ref만 지운다.
- `taggerName`·`taggerEmail`을 생략하면 GitHub가 토큰 사용자로 채운다.
- 오류(모두 `isError: true` 결과): 입력 누락·미등록 인스턴스는 요청하지 않는다. GitHub 오류는 `GitHub '{instance}' {METHOD} {path} failed (HTTP {code}): {message}`, 전송 오류는 `... failed: {message}`. 주석 태그에서 ref 생성만 실패하면 만든 태그 객체 sha를 덧붙인다. Token은 넣지 않으며 재시도하지 않는다.

### GitHub 릴리스 (REST) — `Tools.GitHub.GitHubReleaseTools`

GitHub MCP Server가 노출하지 않는 릴리스 쓰기를 GitHub REST API로 직접 처리한다. 모두 `instance`, `owner`, `repo`가 필수이고 Contents 쓰기 권한이 필요하다. 릴리스는 `releaseId`(정수) 또는 `tag` 중 정확히 하나로 지정한다. `tag`로는 초안(draft) 릴리스를 찾지 못하므로 초안은 `releaseId`로 지정한다.

| Tool | 추가 파라미터 | REST | 힌트 | 반환 |
| --- | --- | --- | --- | --- |
| `github_create_release` | `tagName`(필수), `targetCommitish`, `name`, `body`, `draft`, `prerelease`, `generateReleaseNotes` | `POST releases` | 쓰기 · 외부 접근 | GitHub 릴리스 응답 그대로 |
| `github_update_release` | 릴리스 지정, `tagName`, `targetCommitish`, `name`, `body`, `draft`, `prerelease`(준 값만, 하나 이상) | `PATCH releases/{id}` | 쓰기 · 파괴적 · 멱등 · 외부 접근 | GitHub 릴리스 응답 그대로 |
| `github_delete_release` | 릴리스 지정 | `DELETE releases/{id}` | 쓰기 · 파괴적 · 멱등 · 외부 접근 | `{"deleted":{"releaseId":N}}` |
| `github_upload_release_asset` | 릴리스 지정, `name`(필수), `contentBase64`(필수), `contentType`(기본 `application/octet-stream`), `label` | 릴리스 `upload_url`로 `POST ?name=&label=` | 쓰기 · 외부 접근 | GitHub asset 응답 그대로 |
| `github_delete_release_asset` | `assetId`(정수, 필수) | `DELETE releases/assets/{id}` | 쓰기 · 파괴적 · 멱등 · 외부 접근 | `{"deleted":{"assetId":N}}` |

- `tagName`의 태그가 없으면 GitHub가 `targetCommitish` 기준으로 만든다(초안은 공개할 때). 릴리스를 지워도 태그는 남으며 `github_delete_ref`로 지운다.
- asset 내용은 base64로 주며 디코딩 후 10 MiB(10,485,760 바이트)까지다. MCP 요청은 약 1.33배가 되므로 서버·프록시의 요청 본문 한도도 확인한다.
- 업로드 주소는 릴리스 응답의 `upload_url`을 쓴다. Token을 실어 보내므로 scheme·host·port가 `ApiEndpoint`와 같아야 하며(github.com은 `uploads.github.com` 허용), 아니면 요청 전 오류다.
- 오류 형식·Token 비노출·재시도 없음은 태그·ref Tool과 같다.

## 빌드·테스트

요구 사항: .NET 10 SDK

```bash
dotnet build Biz.Bizadm.MCP.ToolKit.slnx
```

```bash
dotnet test Biz.Bizadm.MCP.ToolKit.slnx
```

폴더 구조:

```
Biz.Bizadm.MCP.ToolKit.slnx
Directory.Build.props        # 공통 TFM·Nullable·ImplicitUsings
Directory.Packages.props     # 중앙 패키지 버전 관리
src/Biz.Bizadm.MCP.ToolKit/  # 라이브러리
tests/Biz.Bizadm.MCP.ToolKit.Tests/  # MSTest
docs/{카테고리}/             # 기능별 방안·설계·개발 문서(toolkit, amp, audio, speaker, storage, agent-resource, redmine)
```

## 라이선스

[MIT](LICENSE)
