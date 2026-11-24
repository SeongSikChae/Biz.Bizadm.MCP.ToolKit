---
상태: 유효
---

# D01_01 마일스톤 Tool 범위

## 상황

GitHub MCP Server에 없는 마일스톤 기능을 REST Tool로 채울 때 어디까지 만들지 정해야 했다. 관련 항목: `R01-R1`~`R01-R5`. GitHub MCP Server의 `ui_get` 설명은 UI용 데이터(라벨·담당자·마일스톤 등)를 가져온다는 것뿐이며, 닫힌 마일스톤이나 진행률(`open_issues`·`closed_issues`)을 주는지는 확인하지 않았다(미확인, 2026-11-24 `github_list_tools`로 `SeongSikChaeGitHub` 인스턴스의 Tool 목록 조회).

## 결정

- 다섯 Tool을 모두 만든다: `github_list_milestones`, `github_get_milestone`, `github_create_milestone`, `github_update_milestone`, `github_delete_milestone`.
- 닫기·다시 열기는 별도 Tool 없이 `github_update_milestone`의 `state`로 한다.
- 이슈에 마일스톤을 붙이는 기능은 만들지 않는다(GitHub MCP Server의 `issue_write`가 한다).

## 이유

- 사용자가 말하지 않음

## 버린 안

| 안 | 버린 이유 |
| --- | --- |
| 쓰기 셋만: 만들기·고치기·지우기 Tool만 두고, 목록·진행률은 GitHub 웹이나 `ui_get`로 확인한다 | 사용자가 말하지 않음 |

## 경위

- 없음

## 코드

- `src/Biz.Bizadm.MCP.ToolKit/Tools/GitHub/GitHubMilestoneTools.cs`: `GitHubMilestoneTools`의 `ListMilestones`, `GetMilestone`, `CreateMilestone`, `UpdateMilestone`, `DeleteMilestone`
