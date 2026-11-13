---
status: done
category: nuget
feature: nuget-tools
phase: 방안
---

# nuget-tools 방안

GitHub 이슈: [#4 NuGet 피드 REST Tool 추가](https://github.com/SeongSikChae/Biz.Bizadm.MCP.ToolKit/issues/4).

## 문제

- ToolKit에 NuGet 피드를 다루는 Tool이 없다. 그래서 패키지 검색, 버전·취약점 확인, 사내 피드 배포(push)와 unlist를 사람이 `dotnet nuget` CLI나 웹 화면에서 처리한다.
- Microsoft 공식 NuGet MCP Server(preview)는 프로젝트의 패키지 버전 관리용이다. 피드 조회, push, unlist는 지원하지 않는다.
- GitHub·Jenkins와 달리 NuGet 피드에는 upstream MCP 서버가 없다. 대신 nuget.org와 사설 피드(Azure Artifacts, GitHub Packages, Nexus, BaGet 등)가 공통 **NuGet Server API v3**를 제공한다.

## 목표

등록한 NuGet 피드 인스턴스의 service index(`index.json`)에서 리소스 주소를 찾고, NuGet Server API v3를 직접 호출해 패키지를 조회하고, push·unlist·relist를 한다.

## 대안

노출 방식:

| 대안 | 판단 |
| --- | --- |
| 전용 MCP Tool 추가 (REST 직접 호출) | **채택**. Jenkins Job 설정 Tool(1.5.0)과 같은 방식. upstream MCP가 없어 중계 게이트웨이를 둘 수 없다 |
| 공식 NuGet MCP Server를 중계 | 피드 조회·쓰기 기능이 없어 목표를 충족하지 못한다 |
| `dotnet nuget` CLI 호출 | 호스트에 SDK가 필요하고 출력 파싱이 불안정하다 |

인스턴스 구성:

| 대안 | 판단 |
| --- | --- |
| 다중 인스턴스 + 필수 `instance` 인자 | **채택(사용자 결정)**. GitHub·Jenkins와 같은 규칙. nuget.org와 사내 피드를 같은 방식으로 등록한다 |
| nuget.org 내장 + 사내 피드만 설정 | 예외 규칙이 생긴다 |

push 입력:

| 대안 | 판단 |
| --- | --- |
| MCP 서버 호스트의 로컬 `.nupkg` 경로 | `dotnet pack` 결과를 바로 올릴 수 있고 큰 파일도 처리한다. 서버가 원격이면 쓸 수 없다 |
| Base64 본문 | 어디서나 동작하지만 큰 패키지는 Tool 인자 크기 제한에 걸린다 |
| 둘 다 지원, 하나만 받음 | **채택(사용자 결정)**. 둘 다 주거나 둘 다 없으면 요청 전 오류 |

## 채택 방안

- 인스턴스 설정: `Name`(필수), `ServiceIndex`(필수, `index.json` 절대 http/https URL), `Description`(선택), `ApiKey`(선택, 쓰기용 `X-NuGet-ApiKey`), `User`/`Password`(선택, Basic 인증. Azure Artifacts·GitHub Packages처럼 읽기에도 인증이 필요한 피드용).
- service index는 인스턴스마다 처음 호출할 때 읽고 캐시한다. 피드가 제공하지 않는 리소스를 쓰는 Tool을 부르면 `isError` 결과를 돌려준다.
- 범위:

| Tool | 리소스(@type) | 용도 | 인증 |
| --- | --- | --- | --- |
| `nuget_list_instances` | - | 등록한 인스턴스 이름·설명·service index 조회 | - |
| `nuget_search_packages` | `SearchQueryService` | 키워드·prerelease 필터로 패키지 검색 | 선택 Basic |
| `nuget_list_versions` | `PackageBaseAddress/3.0.0` | 패키지 ID의 버전 목록 | 선택 Basic |
| `nuget_get_package_metadata` | `RegistrationsBaseUrl` | 버전별 의존성, listed, deprecation, 취약점 | 선택 Basic |
| `nuget_check_vulnerabilities` | `RegistrationsBaseUrl` (`VulnerabilityInfo/6.7.0`은 설계 결정 4로 제외) | 특정 버전의 알려진 취약점 | 선택 Basic |
| `nuget_push_package` | `PackagePublish/2.0.0` (`PUT`) | `.nupkg` 업로드(경로 또는 Base64) | ApiKey |
| `nuget_unlist_package` | `PackagePublish/2.0.0` (`DELETE`) | 버전 unlist | ApiKey |
| `nuget_relist_package` | `PackagePublish/2.0.0` (`POST`) | 버전 relist | ApiKey |

- 쓰기 Tool은 `ApiKey`가 없는 인스턴스로 부르면 요청 없이 `isError`다. ApiKey·Password는 메시지·결과에 넣지 않는다.
- `DELETE`의 동작은 피드마다 다르다. nuget.org는 unlist이고, 일부 사설 피드(BaGet, Nexus 설정 등)는 실제 삭제일 수 있다. Tool 설명과 README에 적는다.
- **호환성**: 기존 GitHub·Jenkins·Redmine 등 Tool과 등록 API는 바꾸지 않는다. `WithNuGetTools`를 새로 추가한다.

## 비범위

ApiKey·Password KMS 암호화(호스트 MCP.Server 책임), MCP.Server 설정 변경, 심볼 패키지(`.snupkg`) push, deprecation 설정·owner 관리(nuget.org 웹 UI 전용), Catalog 변경 이력 조회, `.nupkg` 다운로드, 자동 재시도.
