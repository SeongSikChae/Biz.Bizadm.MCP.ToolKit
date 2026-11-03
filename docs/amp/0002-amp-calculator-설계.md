---
status: done
category: amp
feature: amp-calculator
phase: 설계
---

# amp-calculator 설계

선행: `0001-amp-calculator-방안.md`

## 구조

```
src/Biz.Bizadm.MCP.ToolKit/
  BizadmToolKitMcpServerBuilderExtensions.cs   # 등록 1줄 추가
  Tools/
    AmpTools.cs                                # 앰프 계산 Tool 메서드 모음
  Models/
    Amp/
      AmpMaxOutputResult.cs                    # calculate_amp_max_output 결과 record
tests/Biz.Bizadm.MCP.ToolKit.Tests/
  AmpToolsTests.cs
```

- `AmpTools` 한 클래스에 앰프 계산 Tool 메서드를 모은다.
- 결과 record는 `Models/{도메인}/`에 Tool마다 별도 파일로 둔다. 네임스페이스는 `Biz.Bizadm.MCP.ToolKit.Models.Amp`.
- 이름 규칙: Tool `calculate_amp_{기능}` / 메서드 `Calculate{기능}` / 결과 `Amp{기능}Result`.
- 추후 추가(이번 범위 아님): 입력 전압·부하 임피던스 → 출력 전압·출력 전력 Tool은 `calculate_amp_load_output` / `CalculateLoadOutput` / `AmpLoadOutputResult`로 `AmpTools`에 메서드를 추가한다. `AmpTools` 등록은 이미 되어 있으므로 등록 코드는 바뀌지 않는다.

## 인터페이스

```csharp
namespace Biz.Bizadm.MCP.ToolKit.Tools;

[McpServerToolType]
public sealed class AmpTools
{
    [McpServerTool(Name = "calculate_amp_max_output", UseStructuredContent = true,
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("...")]
    public static AmpMaxOutputResult CalculateMaxOutput(
        [Description("스피커 임피던스(Ω)")] double impedanceOhm,
        [Description("정격 출력(W)")] double powerW,
        [Description("입력 감도(Vrms)")] double inputSensitivityVrms);
}

// Models/Amp/AmpMaxOutputResult.cs
namespace Biz.Bizadm.MCP.ToolKit.Models.Amp;

public sealed record AmpMaxOutputResult(
    double ImpedanceOhm,
    double PowerW,
    double InputSensitivityVrms,
    double InputSensitivityVpp,
    double MaxOutputVrms,
    double MaxOutputVpp,
    double VoltageGainDb);
```

- 클래스는 `static`이 아닌 `sealed class`다. 정적 클래스는 `WithTools<T>()`의 형식 인수로 쓸 수 없다. 메서드는 상태가 없으므로 `static`이다.
- Tool 이름은 `calculate_amp_max_output`(snake_case).
- `UseStructuredContent = true`: 클라이언트에 `outputSchema`와 `structuredContent`를 제공한다.
- 힌트: 계산만 하므로 `ReadOnly`, `Idempotent` true, `Destructive`, `OpenWorld` false.
- 매개변수·Tool에 `[Description]`(System.ComponentModel)을 단다. public API에는 `///` XML 주석을 단다.

## 계산

| 필드 | 식 |
| --- | --- |
| `InputSensitivityVpp` | `inputSensitivityVrms × 2√2` |
| `MaxOutputVrms` | `√(powerW × impedanceOhm)` |
| `MaxOutputVpp` | `MaxOutputVrms × 2√2` |
| `VoltageGainDb` | `20 × log10(MaxOutputVrms / inputSensitivityVrms)` |

입력 3개는 그대로 결과에 담는다. 반올림하지 않는다.

## 입력 검증

- 세 입력 중 하나라도 `0 이하`이거나 유한수가 아니면(`NaN`, `±∞`) `McpException`을 던진다.
- 메시지에 매개변수 이름과 값을 넣는다. 예: `impedanceOhm must be a positive finite number: 0`
- SDK는 `McpException` 메시지를 `isError: true` 결과로 클라이언트에 전달한다.

## 등록

```csharp
public static IMcpServerBuilder WithBizadmToolKit(this IMcpServerBuilder builder)
{
    ArgumentNullException.ThrowIfNull(builder);
    builder.WithTools<AmpTools>();
    return builder;
}
```

- scaffold 설계의 명시 등록 원칙을 따른다. 서버는 `.WithTools<AmpTools>()`로 개별 등록도 할 수 있다.

## 의존성

라이브러리는 추가 패키지 없음. `ModelContextProtocol` 2.3.1만 사용한다.
테스트 프로젝트만 등록 테스트용으로 `Microsoft.Extensions.DependencyInjection` 10.0.14를 추가한다(구현 중 사용자 결정, `0003` U4).

## 결정 목록

1. Tool `calculate_amp_max_output`, 메서드 `CalculateMaxOutput`, 결과 record `AmpMaxOutputResult`(`Models/Amp/`, 네임스페이스 `Biz.Bizadm.MCP.ToolKit.Models.Amp`)
2. `AmpTools`는 `sealed class` + `static` 메서드, 앰프 계산 Tool을 이 클래스에 모음. 이름 규칙 `calculate_amp_{기능}` / `Calculate{기능}` / `Amp{기능}Result`
3. `UseStructuredContent` + ReadOnly/Idempotent 힌트
4. 0 이하·비유한 입력은 `McpException`
5. 반올림 없음
6. `WithBizadmToolKit`에 `WithTools<AmpTools>()` 명시 등록
