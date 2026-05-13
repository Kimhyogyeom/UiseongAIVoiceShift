# AI 무비 디렉터 — 전체 연동 가이드

이 문서만 보고 다른 프로젝트에서도 처음부터 구현할 수 있도록 작성된 단일 레퍼런스입니다.
AI 무비 디렉터 부스 (Booth ID `20`) 관점에서 작성되었지만, 다른 Content 유형 부스 개발에도 그대로 활용 가능합니다.

> 최종 업데이트: 2026-05-12
> 문서와 Swagger 내용이 다른 경우 최신 Swagger UI 기준이 우선합니다.

> **⚠ 2026-04-26 이후 — 비동기 결과 제출 전환 (BREAKING)**
> AI 처리가 필요한 모든 Content 유형 부스(무비 디렉터 포함)는 **결과 제출 API가 HTTP 202 + WebSocket `RESULT_READY` 푸시** 방식으로 변경되었습니다.
> 영상 URL은 더 이상 POST 응답에 들어있지 않고 **WebSocket으로 비동기 전달**됩니다. 자세한 흐름은 §4.1, §4.3, §3.3을 참고하세요.

---

## 0. 부스 기본 정보 (AI 무비 디렉터)

| 항목 | 값 |
|------|-----|
| Booth ID | `20` |
| 부스명 | AI 무비 디렉터 |
| 부스 번호 | 2-11 |
| 부스 유형 | Content (생성형, 50점 고정) |
| 생성물 | AI 영상 |
| Start QR 값 | `experience-start:20` |
| Booth Secret | `bsk_dz9qUuRuLo9b5e2k` |
| 결과 제출 요청 필드 | `director`, `genre`, `prompt` |

---

## 1. 서버 환경

| 환경 | REST Base URL | WebSocket Base URL | Swagger |
|------|---------------|---------------------|---------|
| Local | `http://localhost:8080` | `ws://localhost:8080` | `/swagger-ui` |
| Dev | `https://dev-api.uiseong.ai.kr` | `wss://dev-api.uiseong.ai.kr` | `/swagger-ui` |
| Prod | `https://api.uiseong.ai.kr` | `wss://api.uiseong.ai.kr` | — |

---

## 2. 전체 체험 흐름

```
┌──────────────────────────────────────────────────────────────────────┐
│ [A] 부팅 시 (Unity 단말)                                              │
│   Unity → 서버 : WebSocket Handshake                                  │
│     Header: X-Booth-Id, X-Booth-Secret                               │
│   Server → Unity : 101 Switching Protocols (연결 수립)                 │
│                                                                      │
│ [B] 방문자 체험 시작                                                   │
│   Unity : QR 표시 (experience-start:20)                              │
│   방문자 앱 : QR 스캔                                                  │
│   앱 → 서버 : POST /api/v1/experience/sessions                        │
│   Server : 세션 생성 (PENDING)                                         │
│   Server → Unity (WebSocket) : START_SESSION                         │
│     { sessionId, startToken }                                        │
│   Unity : 체험 UI 진입 → 체험 시작 시점에 아래 메시지 송신                 │
│   Unity → Server (WebSocket) : SESSION_STARTED                       │
│     { sessionId, startToken, startedAt }                             │
│   Server : 상태 전이 (PENDING → IN_PROGRESS)                          │
│   Server → Unity : ACK  (5초 내 미도착 시 재전송)                       │
│                                                                      │
│ [C] 체험 진행 & 결과 제출 (비동기 — 2026-04-26 이후)                    │
│   Unity : 사용자 체험 진행 (장르/시나리오 선택)                           │
│   Unity → Server : POST /api/v1/experience/sessions/{id}/result       │
│     Header: X-Start-Token: {startToken}                              │
│     Body (multipart): director, genre, prompt                        │
│   Server → Unity : 202 Accepted { sessionId } (즉시 응답, 영상 URL 없음) │
│   Server : 상태 전이 (IN_PROGRESS → SUBMITTING)                       │
│   Unity : 로딩 화면 유지, WebSocket 메시지 대기                          │
│   Server : AI 처리 (3~7분) → 세션 완료 (SUBMITTING → COMPLETED)         │
│   Server → Unity (WebSocket) : RESULT_READY                          │
│     { sessionId, qrPayload, result: { score, contents, analysis } }  │
│                                                                      │
│ [D] 결과 확인                                                         │
│   Unity : qrPayload로 결과 QR 표시 (`experience-result:{token}`)       │
│   방문자 앱 : QR 스캔 → GET /api/v1/experience/results?qrValue=…       │
└──────────────────────────────────────────────────────────────────────┘
```

### 2.1 세션 상태 전이

| 상태 | 시점 |
|------|------|
| `PENDING` | 서버가 앱의 세션 생성 요청을 받은 직후 |
| `IN_PROGRESS` | Unity가 `SESSION_STARTED` 송신 → 서버가 `ACK` 응답한 후 |
| `SUBMITTING` | Unity가 결과 제출 API 호출 → 서버가 202 응답한 후 (AI 처리 중) |
| `COMPLETED` | 비동기 처리 완료, 서버가 `RESULT_READY` 송신 |
| `ABORTED` | Unity가 `SESSION_ABORT` 송신 (idle timeout) 또는 서버가 처리 실패 확정 |

---

## 3. WebSocket 프로토콜 (Unity ↔ Server)

Unity 단말은 **부팅 직후 단 하나의 상시 WebSocket 연결**을 유지합니다.
세션별로 연결을 맺지 않습니다.

### 3.1 연결 정보

| 항목 | 값 |
|------|-----|
| 엔드포인트 | `GET /ws/experience` |
| Dev URL | `wss://dev-api.uiseong.ai.kr/ws/experience` |
| Prod URL | `wss://api.uiseong.ai.kr/ws/experience` |
| `X-Booth-Id` (Header) | 숫자 문자열 — AI 무비 디렉터는 `20` |
| `X-Booth-Secret` (Header) | 부스별 사전 공유 secret — `bsk_dz9qUuRuLo9b5e2k` |

### 3.2 Handshake

성공 시 `101 Switching Protocols`. 실패 시 `401 Unauthorized` + `X-Handshake-Reject-Reason` 헤더.

| `X-Handshake-Reject-Reason` | 의미 | 대응 |
|----|----|----|
| `MISSING_AUTH_HEADERS` | `X-Booth-Id` 또는 `X-Booth-Secret` 누락/공백 | 설정값 확인 후 재시도 |
| `INVALID_BOOTH_ID` | `X-Booth-Id`가 숫자로 파싱 실패 | 설정값 수정 후 재시도 |
| `BOOTH_NOT_FOUND` | 서버에 등록되지 않은 `boothId` | 운영자 확인, 재시도 의미 없음 |
| `BOOTH_SECRET_NOT_CONFIGURED` | 서버 측 부스에 secret 미설정 | 운영자 확인, 재시도 의미 없음 |
| `SECRET_MISMATCH` | secret 불일치 | 설정값 확인 후 재시도 |

### 3.3 메시지 — Server → Unity

**`START_SESSION`** — 새 세션 생성 시 수신
```json
{ "type": "START_SESSION", "sessionId": 1001, "startToken": "tk-abc..." }
```

**`ACK`** — `SESSION_STARTED` 또는 `SESSION_ABORT`가 정상 처리된 경우 수신
```json
{ "type": "ACK", "sessionId": 1001 }
```

**`NACK`** — `SESSION_STARTED` 또는 `SESSION_ABORT` 처리 실패 시 수신
```json
{ "type": "NACK", "sessionId": 1001, "reason": "INVALID_START_TOKEN" }
```

`reason` 값:

| 값 | 의미 | 대응 |
|----|----|----|
| `SESSION_NOT_FOUND` | 해당 `sessionId`의 세션이 없음 | 단말 상태 점검 |
| `BOOTH_MISMATCH` | 해당 세션이 이 부스의 세션이 아님 | 단말 상태 점검 |
| `INVALID_START_TOKEN` | `startToken` 불일치 | 단말 상태 점검 |
| `INVALID_STATE` | 세션이 이미 시작/종료된 상태 (또는 `SESSION_ABORT` 대상이 `IN_PROGRESS`가 아님) | 중복 전송 방지 |
| `STARTED_BEFORE_CREATED` | `startedAt`이 세션 생성 시각 이전 | 시계 동기화 확인 |
| `INTERNAL_ERROR` | 서버 내부 오류 | 재시도 또는 운영자 문의 |

**`RESULT_READY`** — 결과 제출 후 비동기 AI 처리가 완료된 경우 수신 (Content 유형 부스용, 2026-04-26 추가)
```json
{
  "type": "RESULT_READY",
  "sessionId": 1001,
  "qrPayload": "experience-result:abc123",
  "result": {
    "score": 50,
    "contents": { "GENERATED_VIDEO": "https://storage.example.com/vid.mp4" },
    "analysis": null
  }
}
```

| 필드 | 의미 |
|----|----|
| `sessionId` | 완료된 세션 ID |
| `qrPayload` | 결과 확인 QR에 인코딩할 값 (`experience-result:{token}`) |
| `result` | 기존 HTTP 200 응답의 `data.result`와 동일한 스키마. `contents` 키 이름은 부스별 상이 (§15.2 표 참조) |

**`RESULT_FAILED`** — 결과 제출 후 비동기 처리가 확정 실패한 경우 수신 (2026-04-26 추가)
```json
{ "type": "RESULT_FAILED", "sessionId": 1001, "reason": "SUBMISSION_FAILED" }
```

확정 실패(입력 오류, 도메인 규칙 위반)에서만 전송. 타임아웃·네트워크 오류 같은 불확실 실패에는 송신되지 않으므로, WebSocket 끊김 후에는 §4.3 `GET /status` 폴링으로 확인.

### 3.4 메시지 — Unity → Server

**`SESSION_STARTED`** — `START_SESSION` 수신 후, 체험 UI가 실제 시작될 때 송신
```json
{
  "type": "SESSION_STARTED",
  "sessionId": 1001,
  "startToken": "tk-abc...",
  "startedAt": "2026-04-17T14:03:22"
}
```

| 필드 | 타입 | 필수 | 비고 |
|----|----|----|----|
| `type` | string | ✔ | 항상 `"SESSION_STARTED"` |
| `sessionId` | number | ✔ | 서버 발급 세션 ID |
| `startToken` | string | ✔ | 수신한 토큰 그대로, 공백 불가 |
| `startedAt` | string | ✔ | ISO-8601 LocalDateTime (timezone 없음) |

**`SESSION_ABORT`** — idle timeout 등으로 진행 중인 세션을 중단할 때 송신 (2026-04-24 추가). `IN_PROGRESS` 상태에서만 유효.
```json
{
  "type": "SESSION_ABORT",
  "sessionId": 1001,
  "startToken": "tk-abc..."
}
```

| 필드 | 타입 | 필수 | 비고 |
|----|----|----|----|
| `type` | string | ✔ | 항상 `"SESSION_ABORT"` |
| `sessionId` | number | ✔ | 중단할 세션 ID |
| `startToken` | string | ✔ | 소유권 검증용 |

서버 응답: 성공 시 `ACK` (세션이 `ABORTED`로 전이), 실패 시 `NACK INVALID_STATE` (이미 제출 중이거나 완료됨 → 중단 포기, 결과 대기 유지).

### 3.5 Close Codes & 재연결 정책

| Code | 의미 | Unity 대응 |
|----|----|----|
| `1003` | 메시지 형식 오류 | 메시지 점검 후 재연결 |
| `1008` | 서버 내부 이상 | 재연결 |
| `4001 SUPERSEDED` | 동일 부스에서 새 연결이 들어와 기존 연결 대체됨 | **재연결 금지** |
| (close code 없이 끊김) | 서버 재시작, 네트워크 장애 | 지수 백오프 재연결 (1s → 2s → 4s → ... 최대 30s) |

재연결 성공 후 `PENDING` 세션은 서버가 자동 재전송합니다.

### 3.6 ACK 타임아웃

- `SESSION_STARTED` 송신 후 5초 내 `ACK`/`NACK` 미수신 시 타임아웃.
- 동일한 `SESSION_STARTED`를 재전송합니다. 서버는 멱등 처리합니다.

### 3.7 성공 흐름 예시

```
Client → Server  GET /ws/experience  (X-Booth-Id:20, X-Booth-Secret:bsk_dz9qUuRuLo9b5e2k)
Server → Client  101 Switching Protocols
Server → Client  {"type":"START_SESSION","sessionId":1001,"startToken":"tk-abc"}
Client → Server  {"type":"SESSION_STARTED","sessionId":1001,"startToken":"tk-abc","startedAt":"2026-04-17T14:03:22"}
Server → Client  {"type":"ACK","sessionId":1001}

(체험 진행 후 결과 제출 — §4.1)
Client → Server  POST /api/v1/experience/sessions/1001/result  (X-Start-Token, multipart)
Server → Client  202 Accepted {"isSuccess":true,"data":{"sessionId":1001}}

(AI 처리 3~7분, Unity는 로딩 화면 유지)

Server → Client  {"type":"RESULT_READY","sessionId":1001,"qrPayload":"experience-result:abc","result":{"score":50,"contents":{"GENERATED_VIDEO":"https://..."},"analysis":null}}
```

---

## 4. REST API — Unity에서 호출하는 것

### 4.1 체험 결과 제출

```
POST /api/v1/experience/sessions/{sessionId}/result
Content-Type: multipart/form-data
X-Start-Token: {startToken}
```

- `sessionId`, `startToken`은 WebSocket `START_SESSION` 메시지의 값을 그대로 사용합니다.
- 각 세션은 **1회만 제출 가능**. 이미 제출된 세션에 재제출하면 409.
- **AI 처리가 필요한 Content 유형 부스(무비 디렉터 포함)는 2026-04-26부터 응답이 HTTP `202 Accepted`로 변경**. 영상 URL은 응답 본문에 없고, WebSocket `RESULT_READY`로 별도 전달됩니다.
- 점수형 부스(Score Fixed/Variable, Score+Content)는 기존대로 동기 `200 OK`.
- 재테스트가 필요하면 Dev 전용 리셋 API(§4.2) 사용.

**요청 필드 (AI 무비 디렉터):**

| 위치 | 필드 | 타입 | 필수 | 설명 |
|----|----|----|----|----|
| path | `sessionId` | long | ✔ | WebSocket으로 받은 세션 ID |
| header | `X-Start-Token` | string | ✔ | WebSocket으로 받은 토큰 |
| body | `director` | string | ✔ | 감독명 |
| body | `genre` | string | ✔ | 장르 enum (§9.4) |
| body | `prompt` | string | ✔ | 사용자 시나리오 텍스트 |

**요청 예시 (multipart form-data):**
```
director: 봉준호
genre: thriller
prompt: 비 오는 골목길에서 벌어지는 추격전
```

#### 4.1.1 응답 코드 분기

| HTTP | 의미 | Unity 처리 |
|----|----|----|
| `200 OK` | 동기 결과 포함 (점수형 부스 전용) | 즉시 `result.contents` 사용 + QR 표시 |
| `202 Accepted` | 비동기 접수 완료 (Content 유형 부스) | 로딩 UI 유지 → WebSocket `RESULT_READY` 대기 |
| `409 Conflict` | 이미 제출된 세션 | 재시도 금지, 에러 안내 후 타이틀 복귀 |
| `503 Service Unavailable` | AI 백엔드 과부하 | 새 세션으로 재체험 유도 |

#### 4.1.2 응답 본문

**200 OK (동기, 점수형 부스):**
```json
{
  "isSuccess": true,
  "trackingId": "550e8400-...",
  "data": {
    "qrPayload": "experience-result:abc123xyz",
    "result": {
      "score": 50,
      "contents": { "GENERATED_VIDEO": "https://..." },
      "analysis": null
    }
  }
}
```

**202 Accepted (비동기, Content 유형):**
```json
{
  "isSuccess": true,
  "trackingId": "550e8400-...",
  "data": { "sessionId": 7004 }
}
```

`data`에 `sessionId`만 들어 있습니다. **영상 URL/qrPayload는 이후 WebSocket `RESULT_READY` 메시지(§3.3)로 전달**됩니다.

| 필드 | 의미 |
|----|----|
| `qrPayload` | 결과 확인 QR에 인코딩할 값. `experience-result:{token}` 형식 |
| `result.score` | 최종 점수 (무비 디렉터는 50점 고정) |
| `result.contents` | 생성 결과물 URL 맵. 점수형 부스는 `{}` |
| `result.analysis` | 서버 분석 결과. AI 블록 크리에이터만 `matchRate` 포함, 그 외 `null` |

**Unity 처리 (Content 유형 부스, 비동기):**
1. 202 수신 → 로딩 UI 유지 (즉시 결과 처리 금지)
2. WebSocket `RESULT_READY` 수신 대기
3. `RESULT_READY.qrPayload`를 QR로 인코딩하여 결과 화면에 표시
4. `RESULT_READY.result.contents.GENERATED_VIDEO`로 생성 영상 URL 획득
5. `RESULT_FAILED` 수신 시 실패 안내 → 타이틀 복귀
6. WebSocket 끊김 또는 600초 경과 시 §4.3 `GET /status` 폴백

### 4.2 체험 세션 리셋 API (Dev 전용)

Dev 프로필에서만 노출. 세션 상태를 `IN_PROGRESS`로 되돌리고 **새 `startToken`을 발급**합니다. 응답의 `tokens`에서 새 토큰을 받아 이후 결과 제출 시 `X-Start-Token`에 사용해야 합니다.

**개별 세션 리셋**
```
POST /api/dev/sessions/reset
Content-Type: application/json

{ "sessionIds": [1001, 1002] }
```

**전체 테스트 세션 리셋**
```
POST /api/dev/sessions/reset/all
```

**응답 예시:**
```json
{
  "isSuccess": true,
  "data": {
    "updatedCount": 2,
    "tokens": {
      "1001": "a3b1c5d9-8f2a-4e1b-9c3d-7e8f6a4b2c1d",
      "1002": "f7e6d4c3-2b1a-4d5e-8f9a-1b2c3d4e5f6a"
    }
  }
}
```

### 4.3 비동기 결과 상태 조회 (WebSocket 폴백)

WebSocket이 끊겼거나 `RESULT_READY`가 600초 이상 도착하지 않을 때 직접 상태를 조회합니다.

```
GET /api/v1/experience/sessions/{sessionId}/status
X-Start-Token: {startToken}
```

**응답 — SUBMITTING (아직 처리 중):**
```json
{
  "isSuccess": true,
  "data": { "status": "SUBMITTING" }
}
```

**응답 — COMPLETED (결과 도착):**
```json
{
  "isSuccess": true,
  "data": {
    "status": "COMPLETED",
    "qrPayload": "experience-result:abc123",
    "result": {
      "score": 50,
      "contents": { "GENERATED_VIDEO": "https://..." }
    }
  }
}
```

**응답 — ABORTED (실패 확정):**
```json
{
  "isSuccess": true,
  "data": { "status": "ABORTED" }
}
```

**Unity 폴백 전략:**
1. WebSocket 끊김 감지 시 → 5초 주기로 `GET /status` 폴링
2. `SUBMITTING` → 계속 폴링
3. `COMPLETED` → `RESULT_READY` 수신과 동일 로직으로 결과 화면 표시
4. `ABORTED` → 실패 안내 → 타이틀 복귀
5. 202 수신 후 누적 600초 경과 시 → 1회 조회 후 결과 없으면 포기 (타이틀 복귀)

---

## 5. REST API — Unity에서 호출하지 않지만 알아야 하는 것

전체 API 목록 (참고):

| Method | Path | 설명 |
|----|----|----|
| POST | `/api/v1/auth/visitor/login` | 방문자 코드 로그인 |
| POST | `/api/v1/visitors/import` | 예약 방문자 일괄 등록 (API Key 필요) |
| POST | `/api/v1/visitors/register` | 현장 방문자 즉시 등록 |
| GET | `/api/v1/booths` | 부스 목록 (커서 페이지네이션) |
| GET | `/api/v1/booths/{boothId}` | 부스 상세 |
| GET | `/api/v1/facility-maps/{floor}` | 시설 지도 (도면 + 마커) |
| POST | `/api/v1/experience/sessions` | 체험 세션 시작 |
| POST | `/api/v1/experience/sessions/{sessionId}/result` | 체험 결과 제출 (**Unity 호출**) |
| POST | `/api/v1/experience/participations` | 참여 체험 기록 |
| GET | `/api/v1/experience/results?qrValue=…` | 체험 결과 상세 조회 |
| GET | `/api/v1/rankings` | 리더보드 (상위 3명 + 내 주변) |
| GET | `/api/v1/rankings/me` | 내 순위 |
| GET | `/api/v1/rankings/me/attempts` | 내 점수 이력 (커서) |
| GET | `/api/v1/contents/me` | 내 콘텐츠 목록 (커서) |

### 5.1 방문자 로그인 (앱)

```
POST /api/v1/auth/visitor/login
Content-Type: application/json

{ "visitorCode": "VC-AVAILABLE" }
```

응답:
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI...",
  "tokenType": "Bearer",
  "expiresIn": 86400,
  "expiresAt": "2026-03-28T14:00:00"
}
```

- JWT Access Token만 사용 (Refresh Token 없음). 유효기간 24시간.
- 이후 인증 API는 `Authorization: Bearer {accessToken}` 헤더 필요.
- 401 수신 시 내부 세션 파기 후 재로그인 화면으로 이동.

### 5.2 테스트 방문자 계정 (Dev 더미 데이터)

| visitor_code | name | status |
|----|----|----|
| `VC-AVAILABLE` | 테스트방문자 | ACTIVE |
| `VC-INACTIVE` | 비활성방문자 | INACTIVE |
| `VC-USER003` ~ `VC-USER010` | 홍길동, 김철수, 이영희, 박민수, 정수연, 최지훈, 강예진, 윤서준 | ACTIVE |

### 5.3 체험 세션 시작 (앱)

```
POST /api/v1/experience/sessions
Content-Type: application/json
Authorization: Bearer {accessToken}

{ "qrPayload": "experience-start:20" }
```

서버가 세션 생성 후 **WebSocket으로 Unity에 `START_SESSION`** 전달.

### 5.4 참여형 부스 (앱)

```
POST /api/v1/experience/participations
Authorization: Bearer {accessToken}

{ "qrPayload": "participation:2" }
```

단말 없이 QR 스캔만으로 50점 고정 처리. 최초 1회만 총점 반영.

### 5.5 예약 방문자 일괄 등록 (외부 연계용)

```
POST /api/v1/visitors/import
X-Api-Key: {발급된 API Key}
Content-Type: application/json

{
  "visitors": [
    { "visitorCode": "VC-0001", "name": "홍길동", "visitDate": "2026-03-28" },
    { "visitorCode": "VC-0002", "name": "김영희", "visitDate": "2026-03-29" }
  ]
}
```

- 최대 1,000건 / 동일 `visitorCode`는 Upsert (갱신) / 도메인 출처: `RESERVATION`
- 401 (A001/A004/A005) — API Key 누락 또는 유효하지 않음

### 5.6 현장 방문자 즉시 등록

```
POST /api/v1/visitors/register
Content-Type: application/json

{ "name": "홍길동" }
```

- 응답으로 서버 생성 `visitorCode` 반환 (예: `WALKIN-20260328-0001`), `visitDate`는 서버 당일
- 도메인 출처: `WALK_IN`

---

## 6. 공통 응답 구조

```json
{
  "isSuccess": true,
  "code": null,
  "message": null,
  "trackingId": "550e8400-e29b-41d4-a716-446655440000",
  "data": {},
  "errors": null
}
```

| 필드 | 타입 | 설명 |
|----|----|----|
| `isSuccess` | boolean | 성공 여부 |
| `code` | string\|null | 에러 코드 (성공 시 null) |
| `message` | string\|null | 에러 메시지 (성공 시 null) |
| `trackingId` | string | 요청 추적 ID (서버 로그 대조용) |
| `data` | T\|null | 응답 데이터 |
| `errors` | array\|null | 필드별 검증 오류 상세 |

### 6.1 커서 페이지네이션 응답

```json
{
  "data": {
    "content": [],
    "nextCursor": "10",
    "hasNext": true
  }
}
```

- 첫 요청은 `cursorId` 없이, 다음 요청부터 응답의 `nextCursor`를 `cursorId` 파라미터로 전달.
- `hasNext == false` → 마지막 페이지.

### 6.2 에러 응답 예시

```json
{
  "isSuccess": false,
  "code": "F001",
  "message": "해당 부스를 찾을 수 없습니다.",
  "trackingId": "550e8400-e29b-41d4-a716-446655440000",
  "data": null,
  "errors": null
}
```

검증 실패 시 `errors` 포함:
```json
{
  "errors": [
    { "field": "sessionId", "message": "세션 ID가 누락되었습니다.", "rejectedValue": null },
    { "field": "score",     "message": "점수는 0 이상이어야 합니다.", "rejectedValue": -10 }
  ]
}
```

---

## 7. 에러 코드 전체

### 7.1 Common

| 코드 | HTTP | 설명 | 대응 |
|----|----|----|----|
| C001 | 400 | 잘못된 요청 | 요청 파라미터 확인 (`errors` 참고) |
| C002 | 500 | 서버 내부 오류 | `trackingId`와 함께 문의 |
| C003 | 415 | 지원하지 않는 Content-Type | 요청 헤더 확인 |
| C004 | 404 | 리소스를 찾을 수 없음 | 요청 URL 확인 |

### 7.2 Auth

| 코드 | HTTP | 설명 | 대응 |
|----|----|----|----|
| A001 | 401 | 인증 정보가 올바르지 않음 | 인증 정보 확인 |
| A002 | 401 | 인증 필요 또는 만료 | 재로그인 후 재시도 |
| A003 | 403 | 접근 권한 없음 | 권한 확인 |
| A004 | 401 | API Key 필요 | `X-Api-Key` 헤더 확인 |
| A005 | 401 | 유효하지 않은 API Key | 발급 키 확인 |
| A006 | 400 | 사용자 정보 오류 | 요청 데이터 확인 |
| A007 | 404 | 등록되지 않은 방문자 코드 | 방문자 코드 확인 |
| A008 | 401 | 비활성화된 방문자 코드 | 상태 확인 |

### 7.3 Visitor

| 코드 | HTTP | 설명 |
|----|----|----|
| V001 | 404 | 방문자 정보를 찾을 수 없음 |
| V002 | 404 | 등록되지 않은 방문자코드 |
| V003 | 400 | 이미 등록된 방문자코드 |
| V004 | 400 | 방문자 정보가 올바르지 않음 |
| V005 | 400 | 비활성화된 방문자 |
| V006 | 400 | 이미 활성화된 방문자 |
| V007 | 400 | 이미 비활성화된 방문자 |
| V101 | 500 | 방문자 코드 순번 발급 실패 |

### 7.4 Facility

| 코드 | HTTP | 설명 |
|----|----|----|
| F001 | 404 | 해당 부스를 찾을 수 없음 (`boothId` 확인) |
| F002 | 404 | 해당 층의 도면 정보를 찾을 수 없음 (`floor` 확인) |

### 7.5 Experience (Unity에서 주로 마주칠 에러)

| 코드 | HTTP | 설명 | 대응 |
|----|----|----|----|
| E004 | 404 | 체험 세션을 찾을 수 없음 | `sessionId` 확인 |
| E006 | 404 | 체험 결과를 찾을 수 없음 | `qrValue` 확인 |
| E020 | 400 | 체험 세션을 시작할 수 없음 | 세션 상태 확인 |
| E023 | 400 | 체험 시작용 QR 형식 오류 | QR 값 확인 |
| E026 | 400 | 결과 조회용 QR 형식 오류 | QR 값 확인 |
| — | 409 | 이미 제출된 세션에 재제출 | 재시도 금지, 타이틀 복귀 |
| — | 503 | AI 백엔드 과부하 (Content 유형) | 안내 + 새 세션 재체험 유도 |

### 7.6 Content

| 코드 | HTTP | 설명 |
|----|----|----|
| CT001 | 400 | 방문자 ID 또는 부스 ID 오류 |
| CT002 | 400 | 부스 이름이 올바르지 않음 |

### 7.7 Ranking

| 코드 | HTTP | 설명 |
|----|----|----|
| R001 | 400 | 점수 시도 정보가 올바르지 않음 |

### 7.8 HTTP 상태별 처리 가이드

| HTTP | 처리 |
|----|----|
| 400 | 에러 메시지 표시, `errors`가 있으면 필드별 표시 |
| 401 | 토큰 만료 / 비활성화 간주 → 세션 파기 후 재로그인 |
| 404 | 대상 없음 안내, 입력값 재확인 유도 |
| 415 | 요청 Content-Type 확인 |
| 500 | 재시도 안내 + `trackingId` 저장 후 운영팀 공유 |

---

## 8. 부스 유형별 정책

### 8.1 유형 분류

| 유형 | 점수 결정 | Unity 점수 전송 | 결과 제출 응답 | 해당 부스 |
|----|----|----|----|----|
| Score Variable | 플레이 결과에 따라 변동 | 필수 (`aqScore`) | 200 (동기) — 블록 크리에이터는 202 (비동기 AI) | 오디세이, 강 건너기, 아바타, 블록 크리에이터 |
| Score Fixed | 서버가 50점 자동 부여 | 불필요 | 200 (동기) | 보물 사냥꾼, 오토 드라이브, 메디컬 센터, 드로잉 쇼, 아카이브 |
| Score + Content | 플레이 결과에 따라 변동 | 필수 + 파일 | 202 (비동기 AI) | 탐정 |
| Content | 서버가 50점 자동 부여 | 불필요, 콘텐츠 생성 데이터만 전송 | **202 (비동기 AI)** — `RESULT_READY` 대기 | 스카이 메이커, 이스케이프 룸, 웰니스 2종, 보이스 시프트, 작가, 스튜디오, **무비 디렉터** |
| Participation | 서버가 50점 자동 부여 (QR 스캔만) | 단말 없음 | — | 인식 원리, 오목 대결, 시력건강, 팩토리 |

**비동기(202) 대상 8개 부스** — AI 처리(fal 등) 3~7분 소요:
탐정, 스카이 메이커, 이스케이프 룸, 보이스 시프트, 작가, 스튜디오, 무비 디렉터, 블록 크리에이터

### 8.2 총점 규칙

- 모든 체험 수행 시마다 **이력은 누적 저장**.
- 부스별 점수는 해당 부스 **최고점 기준**으로 관리.
- 총점은 부스별 최고점이 기존 값보다 **상승한 경우에만 갱신**.
- 동일/더 낮은 점수: 이력만 저장, 총점 변동 없음.
- 50점 고정 부스 (Content / Score Fixed / Participation): **최초 1회만 50점 반영**, 이후 반복 체험은 이력만.

---

## 9. 전체 부스 매핑

### 9.1 Booth ID / QR / Secret

| Booth ID | No. | 부스명 | 유형 | Start QR | Booth Secret |
|----|----|----|----|----|----|
| 1 | 1-1 | AI 오디세이 | Score Variable | `experience-start:1` | `bsk_IadcHjnc7jdXPK6U` |
| 2 | 1-2 | AI 인식 원리 | Participation | `participation:2` | — |
| 3 | 1-3 | AI 오목 대결 | Participation | `participation:3` | — |
| 4 | 1-4 | AI 보물 사냥꾼 | Score Fixed | `experience-start:4` | `bsk_suj5fEBfmktKdN7K` |
| 5 | 1-5 | AI 강 건너기 | Score Variable | `experience-start:5` | `bsk_W7UQWGmV6JrDuJo4` |
| 6 | 1-6 | AI 아바타 | Score Variable | `experience-start:6` | `bsk_JrXrKS5AvXlGc5qi` |
| 7 | 1-7 | AI 탐정 | Score + Content | `experience-start:7` | `bsk_E8UDSOjftqYlC4y3` |
| 8 | 1-8 | AI 스카이 메이커 | Content | `experience-start:8` | `bsk_bWUcrEGcYXit1nHW` |
| 9 | 1-9 | AI 이스케이프 룸 | Content | `experience-start:9` | `bsk_F0vvfmOgoB3FyG53` |
| 10 | 2-1 | AI 웰니스 센터_신체균형 | Content | `experience-start:10` | `bsk_opMZdnESTWz3sVXS` |
| 11 | 2-2 | AI 웰니스 센터_스트레스 지수 | Content | `experience-start:11` | `bsk_QEyEGLdzVltFqFCn` |
| 12 | 2-3 | AI 웰니스 센터_시력건강 | Participation | `participation:12` | — |
| 13 | 2-4 | AI 팩토리 | Participation | `participation:13` | — |
| 14 | 2-5 | AI 오토 드라이브 | Score Fixed | `experience-start:14` | `bsk_EC3qOlS24PGUxTG8` |
| 15 | 2-6 | AI 메디컬 센터 | Score Fixed | `experience-start:15` | `bsk_055f4LYxXt4T9FIh` |
| 16 | 2-7 | AI 드로잉 쇼 | Score Fixed | `experience-start:16` | `bsk_j671CPZerdUt5H56` |
| 17 | 2-8 | AI 보이스 시프트 | Content | `experience-start:17` | `bsk_n2yTvziKEKs3xNxO` |
| 18 | 2-9 | AI 작가 | Content | `experience-start:18` | `bsk_9VT24j4qYSTxrzG9` |
| 19 | 2-10 | AI 스튜디오 | Content | `experience-start:19` | `bsk_LfBgjQ2z3frHcrDY` |
| **20** | **2-11** | **AI 무비 디렉터** | **Content** | **`experience-start:20`** | **`bsk_dz9qUuRuLo9b5e2k`** |
| 21 | 2-12 | AI 블록 크리에이터 | Score Variable | `experience-start:21` | `bsk_TOPWBMvvinsvZZU1` |
| 22 | 2-13 | AI 아카이브 | Score Fixed | `experience-start:22` | `bsk_5p9EUPzbivPl1YzD` |

### 9.2 유형별 결과 제출 요청 필드

| 유형 | 공통 | 추가 필드 |
|----|----|----|
| Score Variable | `X-Start-Token` | `aqScore` |
| Score Fixed | `X-Start-Token` | 없음 |
| Score + Content (탐정) | `X-Start-Token` | `aqScore`, `originalImage` |
| Content — 스카이 메이커 | `X-Start-Token` | `temperature`, `humidity`, `pressure`, `windSpeed`, `cloudCover`, `precipitation` |
| Content — 이스케이프 룸 | `X-Start-Token` | `originalImage`, `targetImage` |
| Content — 스튜디오 | `X-Start-Token` | `theme`, `prompt` |
| **Content — 무비 디렉터** | `X-Start-Token` | **`director`(자유 문자열), `genre`(enum), `prompt`(자유 문자열)** |
| Content — 웰니스 2종, 보이스 시프트, 작가 | `X-Start-Token` | 미정 |

> Booth Secret은 **WebSocket Handshake 시에만** 사용됩니다. 결과 제출 시에는 `X-Start-Token` 헤더만 사용합니다.

### 9.3 참고 — AI 탐정 `artStyle`

임시로 아래 4개만 설정됨 (실제 명화 리소스 확정 후 변경 예정):
`MONA_LISA`, `GIRL_WITH_A_PEARL_EARRING`, `THE_SCREAM`, `VAN_GOGH_SELF_PORTRAIT`

### 9.4 AI 무비 디렉터 `genre` enum

백엔드가 받는 `genre` 값은 **소문자 영문 enum 10종** 중 하나여야 합니다. 한글 그대로 보내면 `CT013` 에러 ("지원하지 않는 장르입니다").

`action`, `comedy`, `drama`, `horror`, `sf`, `romance`, `thriller`, `fantasy`, `animation`, `documentary`

현재 Unity 프로젝트의 한글 → enum 매핑 ([GameManager.cs](../Scripts/Manager/GameManager.cs) `MapGenreToEnum`):

| UI 표시명 | 전송 값 |
|----|----|
| `SF 공상과학` | `sf` |
| `액션 스릴러` | `thriller` |
| `로맨틱 코미디` | `romance` |
| `호러 미스터리` | `horror` |
| `다큐멘터리` | `documentary` |
| `뮤지컬` | `drama` (직접 매핑 없음) |
| `직접입력` | `drama` |

---

## 10. 시설 지도 마커 좌표

`GET /api/v1/facility-maps/{floor}` 응답의 `booths[].location.x`, `booths[].location.y`는 도면 이미지 내 **0.0 ~ 1.0 상대 좌표**입니다.

```js
const markerX = imageWidth  * marker.location.x;   // 0.35 → 1000px 기준 350px
const markerY = imageHeight * marker.location.y;   // 0.72 → 1000px 기준 720px
```

### 최근 Breaking Change (중요)

- **`GET /api/v1/booths/{boothId}`**: `data.location` 객체 제거. 층 정보는 `data.floor`로 이동. `x`, `y`는 상세 응답에서 제공하지 않음.
- **`GET /api/v1/facility-maps/{floor}`**: `data.booths[].location.floor` 제거. 층 정보는 상위 `data.floor` 사용. 마커 좌표 `x`, `y`는 유지.
- **`GET /api/v1/admin/facility-maps/{floor}`**: 응답이 `{ "imageUrl": "..." }`만 반환하도록 축소.

---

## 11. 브라우저(웹) 연동 시 CORS

서버 간 호출은 CORS 설정 불필요.
브라우저에서 직접 호출하는 경우 허용 도메인 등록 필요 — 프론트엔드 도메인 주소 사전 전달. (예: `http://localhost:5173`)

---

## 12. Unity 프로젝트 구현 가이드

### 12.1 파일 구조 (구현 완료)

```
Assets/
├── Plugins/
│   └── zxing.dll                        # ZXing.Net QR 라이브러리
├── Scenes/
│   ├── SampleScene.unity                # 부스 메인 씬
│   └── VideoDisplayScene.unity          # 디스플레이 PC용 씬 (§14 참조)
├── Scripts/
│   ├── Manager/
│   │   ├── GameManager.cs               # 전체 흐름, 패널 전환, 버튼 이벤트, 로딩/결과/영상 제어
│   │   ├── APIManager.cs                # REST 호출 (결과 제출) — X-Start-Token 헤더, multipart
│   │   ├── WebSocketClient.cs           # WebSocket 상시 연결 (Handshake, ACK/NACK, 재연결)
│   │   ├── QRGenerator.cs               # QR 코드 생성 (ZXing)
│   │   └── DisplayPushSender.cs         # 영상 완성 시 UDP 브로드캐스트 (§14)
│   ├── UI/
│   │   ├── GenreButton.cs               # 장르 버튼 클릭 핸들러
│   │   ├── ExampleButton.cs             # 시나리오 예시 버튼 (Inspector editable)
│   │   ├── BouncingText.cs              # TMP 텍스트 글자별 통통 튀는 애니메이션 (§12.5)
│   │   └── RunnerGame.cs                # 로딩 중 플레이 가능한 러너 미니게임 (§12.6, 선택)
│   └── Display/                          # 디스플레이 PC 전용 (§14)
│       ├── VideoQueueDisplay.cs         # 큐 5개 관리 + 페이드 인/아웃 재생
│       └── VideoPushReceiver.cs         # UDP 수신 (부스 → 디스플레이)
└── Docs/
    └── AI_MovieDirector_API_Guide.md
```

### 12.2 Unity 씬 구조 (필수 GameObject)

| GameObject | 부착 스크립트 | 역할 |
|----|----|----|
| `WebSocketClient` | `WebSocketClient.cs` | 부팅 시 WebSocket 연결, 세션 송수신, 재연결 |
| `APIManager` | `APIManager.cs` | 결과 제출 REST 호출 |
| `QRGenerator` | `QRGenerator.cs` | QR 이미지 생성 (ZXing) |
| `GameManager` | `GameManager.cs` | 전체 플로우 제어 (패널 10개 전환, 버튼 이벤트) |

`WebSocketClient` / `APIManager` / `QRGenerator`는 `DontDestroyOnLoad` + Singleton 패턴.

**패널 10개 (Canvas 하위):**
1. `TitlePanel` — 타이틀 화면 (터치 유도)
2. `QRPanel` — 부스 QR 표시 (`experience-start:20`)
3. `GenrePanel` — 장르 선택 (6종 + 직접입력)
4. `ConfirmPanel` — 장르 확인 팝업
5. `ScenarioPanel` — 시나리오 입력 (TMP_InputField + 글자수)
6. `ExamplePanel` — 예시 시나리오 선택 팝업
7. `ScenarioConfirmPanel` — 시나리오 확인 팝업
8. **`LoadingPanel`** — AI 영상 생성 중 대기 화면 (로딩바 + 안내 텍스트)
9. **`ResultPanel`** — 결과 영상 재생 (영상 + 컨트롤 + 진행바 + 시간 + 결과 QR 저장 버튼)
10. **`ResultQRPanel`** — 결과 QR 팝업 (방문자 앱이 스캔)

모든 홈 버튼은 `GameManager.OnHomeClick` → `ResetToTitle()` 호출 → 전체 패널 초기화.

### 12.3 구현 완료 체크리스트

**기본 플로우:**
- [x] QR 생성/표시 (ZXing) — `experience-start:20`
- [x] 타이틀 → QR → 장르 선택 화면 플로우
- [x] 장르 6종 버튼 + 확인 팝업
- [x] 직접 입력 → 시나리오 패널 + 예시 4종 + 확인 팝업
- [x] 페이드 전환 (`CanvasGroup.alpha` 0↔1)

**WebSocket 연동:**
- [x] 부팅 시 상시 연결 + `Application.runInBackground = true`
- [x] `X-Booth-Id` / `X-Booth-Secret` 헤더 Handshake
- [x] `X-Handshake-Reject-Reason` 에러 로깅
- [x] `START_SESSION` 수신 → QR 패널일 때만 장르 패널로 전환
- [x] `SESSION_STARTED` 송신 (`yyyy-MM-ddTHH:mm:ss.fff` 밀리초 정밀도)
- [x] `ACK` 수신 확인 + `NACK` reason별 로깅
- [x] 5초 ACK 타임아웃 → 동일 메시지 재전송
- [x] Close code `4001 SUPERSEDED` 재연결 금지
- [x] 비정상 끊김 시 지수 백오프 재연결 (1 → 2 → 4 → … → 30s)

**결과 제출:**
- [x] `POST /api/v1/experience/sessions/{id}/result`
- [x] `X-Start-Token` 헤더 (body에 `boothSecret` 넣지 않음)
- [x] `multipart/form-data` (`director`, `genre`, `prompt`)
- [x] 한글 장르 → 영문 enum 매핑 (`sf`, `thriller`, `romance`, `horror`, `documentary`, `drama`)
- [x] 긴 타임아웃 (기본 600초)
- [x] **응답 코드 분기 200/202/409/503** (2026-04-26 비동기 전환 대응)

**결과 처리:**
- [x] 로딩 패널 전환 (2분 선형 0→95% → 창작 대기 0.95→0.99 creep)
- [x] **202 수신 시 로딩 유지** (즉시 결과 처리 금지) — `RESULT_READY` 대기
- [x] **WebSocket `RESULT_READY` 수신 시** 0.5초 부드럽게 100% 채움 → 1.2초 유지 → 결과 패널 전환
- [x] **WebSocket `RESULT_FAILED` 수신 시** 실패 안내 후 타이틀 복귀
- [x] **영상 로컬 다운로드 후 재생** (Windows VideoPlayer HTTPS 버그 우회)
- [x] VideoPlayer 첫 프레임만 렌더링 후 일시정지 (자동 재생 X)
- [x] 시작 / 일시정지 버튼
- [x] 재생 진행바 (`fillAmount = time/length`) + 시간 텍스트 (`M:SS / M:SS`)
- [x] 결과 QR 팝업 (`qrPayload`를 QR로 인코딩)
- [x] 모든 홈 버튼 `ResetToTitle()` 일원화

**UI 애니메이션:**
- [x] `BouncingText.cs` — 글자별 사인곡선 통통 튀는 효과 (재사용 컴포넌트)

**남은 선택 사항:**
- [ ] 에러 화면 UI (API 실패 시 사용자용 재시도 UI — 현재는 타이틀 복귀만)
- [ ] 결과 화면 자동 타이머 (방문자가 놓고 가면 30초 뒤 타이틀 복귀)
- [ ] 백엔드 요청: AI 영상 길이 5초 → 20초 조정 (Unity 코드 수정 불필요)
- [ ] **`GET /status` 폴링 폴백** (WebSocket 끊김 또는 600초 초과 시) — §4.3
- [ ] **`SESSION_ABORT` 송신** (idle timeout 60초 이상 또는 사용자가 홈 버튼) — §3.4

### 12.4 핵심 구현 결정 & 함정 (Gotchas)

처음 구현 시 만날 수 있는 주요 함정들. 같은 실수 반복하지 않게 전부 명시.

**① `Application.runInBackground = true` 필수**

Unity 에디터는 창이 포커스를 잃으면 Update가 거의 멈춤. PowerShell/방문자 앱에서 API 호출하는 동안 Unity가 백그라운드면 WebSocket 메시지를 큐에는 받지만 `DispatchMessageQueue`가 안 돌아서 이벤트가 안 발생. 에디터 포커스 클릭해야만 동작하는 것처럼 보임. `WebSocketClient.Awake()`에서 설정.

**② `startedAt`은 밀리초 정밀도로**

```csharp
startedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff")
```

초 단위로 보내면 서버의 `createdAt`과 같은 초 내에서 클라 시각이 더 작아서 `STARTED_BEFORE_CREATED` NACK 발생. `.fff`로 밀리초 포함 필수.

**③ `boothSecret`은 WebSocket Handshake 전용**

결과 제출 API body에 `boothSecret` 넣지 말 것. `X-Start-Token` 헤더만 사용. 구 스펙에서 넘어왔다면 코드 확인.

**④ 장르는 영문 enum 소문자로**

백엔드는 한글 "SF 공상과학"을 받지 않음. `CT013` 에러 발생. 반드시 `sf`, `thriller` 등으로 매핑 (§9.4 참조).

**⑤ 영상은 로컬 파일로 받아서 재생**

Unity의 Windows VideoPlayer (WMF 기반)는 일부 HTTPS 스트리밍 URL에서 `WindowsVideoMedia error 0x80004004` 발생. fal.media, S3 등 외부 미디어 서버 URL은 대부분 실패.

```csharp
// UnityWebRequest로 먼저 로컬 다운로드
string localPath = Path.Combine(Application.temporaryCachePath, $"video_{DateTime.Now:yyyyMMddHHmmss}.mp4");
using (var req = UnityWebRequest.Get(url)) {
    req.downloadHandler = new DownloadHandlerFile(localPath);
    yield return req.SendWebRequest();
}
// videoPlayer.url = localPath;
```

**⑥ RenderTexture는 코드에서 동적 생성**

Inspector로 RenderTexture 에셋을 만들고 VideoPlayer + RawImage 양쪽에 수동 연결하는 방식은 실수 많음. 영상 해상도도 알 수 없음.

```csharp
videoRT = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
videoRT.Create();
videoPlayer.targetTexture = videoRT;
videoDisplayImage.texture = videoRT;
videoPlayer.prepareCompleted += vp => {
    // 실제 영상 해상도에 맞춰 재생성
    if (videoRT.width != vp.width || videoRT.height != vp.height) {
        videoRT.Release();
        videoRT = new RenderTexture((int)vp.width, (int)vp.height, 0, RenderTextureFormat.ARGB32);
        videoRT.Create();
        vp.targetTexture = videoRT;
        videoDisplayImage.texture = videoRT;
    }
};
```

**⑦ 첫 프레임만 렌더하고 정지**

`Prepare()` 후 `Play()`만 부르면 바로 재생 시작. 첫 프레임 정지 상태를 원하면 Play → 다음 프레임 → Pause + time=0 순서로.

```csharp
void OnVideoPrepared(VideoPlayer vp) {
    vp.Play();
    StartCoroutine(PauseAfterFirstFrame());
}
IEnumerator PauseAfterFirstFrame() {
    yield return null;
    videoPlayer.Pause();
    videoPlayer.time = 0;
}
```

**⑧ 로딩바는 예측 불가 duration용 3단계 구성**

AI 생성 시간은 매번 다름 (30초~3분). 단순 선형 또는 무한 루프는 UX가 나쁨.

1. **Phase 1 (대기)**: 0 → 0.95 선형 (예: 120초 동안) — "진행 중" 느낌
2. **Phase 2 (지연)**: 0.95 → 0.99 매우 느리게 creep — "거의 다 됐음" 느낌 (응답 지연 시에도 멈춰보이지 않음)
3. **Phase 3 (완료)**: 응답 받은 순간 0.5초 동안 현재값 → 1.0 부드럽게 + 1.2초 유지 → 결과 전환

**⑨ WebSocket은 세션당이 아니라 부스당 1개**

부팅 시 1회 연결, 여러 세션(여러 방문자)을 순차 수신. 세션 종료되어도 연결은 유지. 세션마다 연결을 새로 맺지 말 것.

**⑩ 세션 1회 제출 제약 + Dev 리셋 시 새 startToken**

각 세션은 한 번만 제출 가능. 실패 후 재시도하려면 `/api/dev/sessions/reset`으로 새 `startToken`을 받아야 함 (응답 `data.tokens[sessionId]`).

**⑪ 이전 세션 메시지 덮어쓰기 주의**

Server가 PENDING 세션들을 연달아 보낼 수 있음 (예: 재연결 직후). 첫 세션 처리 중 두 번째 세션이 도착하면 `CurrentSessionId`가 덮어씌워져 첫 세션 `SESSION_STARTED`가 유실될 수 있음. 실사용(방문자 1명씩 스캔)에선 엣지 케이스지만 개발 중엔 발생. Dev API로 리셋하며 진행.

**⑫ 결과 제출은 비동기 — 202 응답으로는 영상 URL이 안 옴 (2026-04-26 변경)**

Content 유형 부스(AI 처리 필요)는 결과 제출 API가 **즉시 202 Accepted**를 반환하고, 응답 본문에는 `data.sessionId`만 들어있음. 실제 영상 URL은 **WebSocket `RESULT_READY` 메시지로 별도 도착** (3~7분 후).

```
POST /sessions/{id}/result → 202 {"data":{"sessionId":7004}}
(...AI 처리 3~7분...)
WS Recv RESULT_READY → 영상 URL + qrPayload 도착
```

Unity 처리 흐름:
- `APIManager` — HTTP status 분기: 200(동기 결과)/202(비동기 대기)/409(중복)/503(과부하)
- `WebSocketClient` — `RESULT_READY`/`RESULT_FAILED` 메시지 핸들러 + 이벤트
- `GameManager` — `OnResultAccepted`(202)는 로딩 유지, `OnResultReady`(WS)에서 결과 패널 전환

202를 받자마자 결과 패널로 전환하면 [GameManager] video URL 비어있음 에러가 발생함. 반드시 WebSocket 메시지 대기해야 함.

### 12.5 UX 보조 컴포넌트

**① BouncingText (`Assets/Scripts/UI/BouncingText.cs`)**

TMP 텍스트의 **글자가 하나씩 사인 곡선으로 위로 튕겼다 내려오는 애니메이션**. 원본 vertex 좌표 백업 후 매 프레임 offset 적용 → 종료 시 복구하는 방식이라 레이아웃 안 꼬임.

사용: 대상 TMP_Text GameObject에 `Add Component` → `Bouncing Text`. 파라미터:
- `bounceHeight` (10) — 튕김 높이
- `bounceDuration` (0.4) — 한 글자 1회 튕김 시간
- `perCharDelay` (0.07) — 글자 사이 지연 (파도 느낌)
- `cycleGap` (1.5) — 한 사이클 종료 후 대기
- `loop` (true) — 반복 여부

추천 적용처: 타이틀 "터치해주세요", 로딩 안내, 결과 "완성됐어요" 같은 눈에 띄게 하고 싶은 텍스트.

**② 결과 영화 제목 (`GameManager.BuildResultTitle`)**

결과 패널에 표시할 영화 제목을 사용자 선택/입력에서 자동 생성:

| 선택 | 제목 |
|----|----|
| 장르 버튼 선택 | 그 장르 한글 이름 그대로 (`"SF 공상과학"`, `"호러 미스터리"` 등) |
| 직접 입력 → 시나리오 ≤ 20자 | 시나리오 전체 |
| 직접 입력 → 시나리오 > 20자 | 앞 20자 + `…` |
| 직접 입력 → 빈 문자열 | `"내 이야기"` |
| 장르 없음 (이상 케이스) | `"AI 영화"` |

Inspector에서 `Result Title Text`(TMP_Text)와 `Title Max Chars`(기본 20) 조정 가능. `SubmitToServer` 시점에 `currentResultTitle` 세팅 → 결과 패널 전환 시 반영.

**③ 영상 재생 컨트롤 UI**

Result Panel에 추가된 재생 관련 UI 요소:
- **시작 / 일시정지 버튼** → `GameManager.OnVideoPlayClick` / `OnVideoPauseClick`
- **재생 진행바** (Image, Filled/Horizontal) — `Update`에서 매 프레임 `fillAmount = time / length`로 갱신
- **현재/총 시간 TMP_Text** — `M:SS` 형식, `FormatTime` 유틸 함수로 변환

연결 필드: `videoProgressFill`, `videoTimeCurrent`, `videoTimeTotal`. `Update`에서 `videoPlayer.isPrepared` 체크 후 갱신하므로 준비되지 않은 영상에는 영향 없음.

### 12.6 로딩 중 미니게임 (선택 기능)

AI 영상 생성이 1~3분 걸려서 로딩 화면이 지루한 문제를 해결하는 러너 게임 (`Assets/Scripts/UI/RunnerGame.cs`). 동전 먹기 + 장애물 회피 + 시간 지남에 따라 가속. Loading Panel 안에 배치.

**핵심 특징:**
- UI 기반 (물리엔진 X, `RectTransform.anchoredPosition`만 사용)
- 플레이어 Y = Editor에 배치한 `player.anchoredPosition.y` 자동 사용 (땅 위치)
- 동전 Y 스폰 — 항상 땅 위 (`groundY + halfH + random(coinMinY, coinMaxY)`)
- 장애물 Y = 플레이어 Y (정면 충돌)
- 속도: `currentSpeed = min(maxSpeed, coinSpeed + speedIncreasePerSecond × elapsedTime)`
- `GetWorldCorners` 기반 AABB 충돌 (anchor/pivot/parent 무관)
- 히트박스 스케일 파라미터로 관대함 조절 (`playerHitboxScale`, `obstacleHitboxScale`, `coinHitboxScale`)
- 게임오버 시 별도 `restartButton` (GameOverOverlay 안) → raycast 차단 회피
- Loading Panel OnDisable 시 자동 정지 (`OnDisable`에서 `StopGame`)

**Inspector 핵심 필드:**
- Play Area (RectTransform) — 게임 영역
- Spawn Container (RectTransform) — 동적 생성물 부모 (Hierarchy 정리용)
- Player, Coin Prefab, Obstacle Prefab
- Score Text, Final Score Text
- Start Button, Restart Button, Jump Button
- Ready Overlay, Game Over Overlay
- Jump Force (900) / Gravity (2500)
- Coin Speed (400) / Speed Increase Per Second (10) / Max Speed (1000)
- Coin/Obstacle Interval Min/Max
- Coin Min/Max Y (점프 가능 높이 기준)
- Hitbox Scale 3종 (Player 0.7, Obstacle 0.8, Coin 1.1)

**적용 여부**: Content 유형 (생성에 오래 걸리는) 부스에 권장. Score Fixed 같은 즉시 응답 부스는 로딩 자체가 거의 없어서 불필요.

---

## 13. 개발 테스트 절차 (Dev Testing Guide)

**목적**: 방문자 앱 없이도 Unity 클라이언트의 전체 흐름을 검증. PowerShell로 방문자 역할을 대신 수행.

### 13.1 전제 조건

- Unity 프로젝트 루트에 `WebSocketClient`, `APIManager`, `GameManager` GameObject가 배치되어 있어야 함
- Unity NativeWebSocket 패키지 설치됨 (`Packages/manifest.json`에 `com.endel.nativewebsocket`)
- Windows PowerShell 사용 (Git Bash의 `curl`도 가능하나 본 문서는 PowerShell 기준)
- Dev 서버 접근 가능 (`https://dev-api.uiseong.ai.kr`)

### 13.2 초기 프로젝트 세팅 (처음 시작 시)

1. **NativeWebSocket 설치**
   - Unity 메뉴 `Window` → `Package Manager`
   - `+` → `Install package from git URL...`
   - `https://github.com/endel/NativeWebSocket.git#upm`

2. **씬에 GameObject 3개 생성**
   - `WebSocketClient` (빈 GameObject + `WebSocketClient.cs` 부착)
   - `APIManager` (빈 GameObject + `APIManager.cs` 부착)
   - `GameManager` (UI 연결용, 기존 프로젝트에서 이어받음)

3. **Inspector 값 확인** (모두 기본값으로 사용 가능)
   - WebSocketClient: Base URL `wss://dev-api.uiseong.ai.kr`, Booth Id `20`, Booth Secret `bsk_dz9qUuRuLo9b5e2k`
   - APIManager: Base URL `https://dev-api.uiseong.ai.kr`, Timeout Seconds `600`

4. **씬 저장 후 Play 눌러 WebSocket 연결 확인**
   - 콘솔에 `[WS] Connected (handshake OK, waiting for START_SESSION)` 뜨면 성공

### 13.3 PowerShell 주의사항

| 항목 | 내용 |
|----|----|
| `curl` | PowerShell의 `curl`은 `Invoke-WebRequest`의 별칭. curl 문법 안 통함 |
| 해결 | `curl.exe` 로 명시하거나, **`Invoke-RestMethod` 사용 권장** |
| JSON body | `-Body '{"key":"value"}'` 형식이 가장 안전 (작은따옴표 + JSON) |
| 세션 변수 | `$token` 같은 변수는 **PowerShell 창 닫으면 사라짐**. 매번 재로그인 필요 |

### 13.4 로그인 & 토큰 발급

```powershell
$login = Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/v1/auth/visitor/login" -Method POST -Body '{"visitorCode":"VC-AVAILABLE"}' -ContentType "application/json"
$token = $login.data.accessToken
$token  # 토큰 확인
```

**테스트 계정 코드** (`§5.2`에 전체 목록):
- `VC-AVAILABLE` — ACTIVE 기본 테스트 계정
- `VC-INACTIVE` — 비활성 계정 (401 테스트용)
- `VC-USER003` ~ `VC-USER010` — 추가 ACTIVE 계정 8개

### 13.5 세션 생성 (방문자 QR 스캔 시뮬레이션)

**Unity Play 상태이고 QR 패널이 활성화된 상태**에서 실행해야 화면 전환까지 확인 가능:

```powershell
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/v1/experience/sessions" -Method POST -Body '{"qrPayload":"experience-start:20"}' -ContentType "application/json" -Headers @{ "Authorization" = "Bearer $token" } | ConvertTo-Json -Depth 5
```

성공 응답:
```json
{ "isSuccess": true, "data": { "sessionId": 70XX } }
```

동시에 Unity 콘솔에서 확인할 이벤트:
```
[WS] Recv: {"type":"START_SESSION","sessionId":70XX,"startToken":"..."}
[GameManager] Session begin received (sessionId=70XX)
[WS] Send: {"type":"SESSION_STARTED","sessionId":70XX,"startToken":"...","startedAt":"..."}
[WS] Recv: {"sessionId":70XX,"type":"ACK"}
[WS] ACK sessionId=70XX
```

### 13.6 세션 리셋 (Dev 전용)

세션이 실패해서 `PENDING`/`IN_PROGRESS` 상태로 남으면 재사용 불가. 리셋으로 복구.

**개별 세션 리셋:**
```powershell
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/dev/sessions/reset" -Method POST -Body '{"sessionIds":[7021,7022]}' -ContentType "application/json" | ConvertTo-Json -Depth 5
```

**하드코딩 테스트 세션 전체 리셋:**
```powershell
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/dev/sessions/reset/all" -Method POST | ConvertTo-Json -Depth 5
```
- 주의: 동적으로 생성된 세션(방문자 앱 플로우)은 리셋되지 않음. 반드시 개별 리셋 사용
- 리셋 후 세션은 `IN_PROGRESS` 상태가 되며 **새 `startToken` 발급** (응답 `tokens` 맵 참고)

### 13.7 결과 제출 확인 (Unity에서 자동 호출)

Unity에서 장르 선택 → "네!" 누르면 자동으로 `POST /api/v1/experience/sessions/{sessionId}/result` 요청. 콘솔 흐름 (2026-04-26 이후 비동기):

```
[GameManager] 결과 제출 요청 sessionId=70XX genre=SF 공상과학->sf promptLen=N
[API] POST https://dev-api.uiseong.ai.kr/api/v1/experience/sessions/70XX/result director=AI genre=sf ...
[API] HTTP 202 response: {"isSuccess":true,"trackingId":"...","data":{"sessionId":70XX}}
[API] 202 ACCEPTED sessionId=70XX — WebSocket RESULT_READY 대기
[GameManager] 결과 비동기 접수됨 sessionId=70XX — RESULT_READY 대기

(AI 처리 3~7분, 로딩 화면 유지)

[WS] Recv: {"type":"RESULT_READY","sessionId":70XX,"qrPayload":"experience-result:...","result":{...}}
[WS] RESULT_READY sessionId=70XX qrPayload=experience-result:... video=https://...
[GameManager] 결과 수신 성공 qrPayload=... video=...
```

**중요**: Content 유형(무비 디렉터 포함)은 AI 생성이라 **3~7분 소요**. Unity 화면엔 로딩 UI가 없으면 "멈춘 것처럼" 보임 — 정상. **202 직후 결과 패널로 전환되지 않는 것이 의도된 동작**.

#### 13.7.1 점수형 부스 (200 동기 응답)

블록 크리에이터/탐정을 제외한 Score Variable/Fixed 부스는 기존대로 200 동기 응답:
```
[API] HTTP 200 response: {"isSuccess":true,"data":{"qrPayload":"...","result":{"score":50,"contents":{...}}}}
[API] 200 SUCCESS qrPayload=... score=50 video=https://...
[GameManager] 결과 수신 성공 ...
```

### 13.8 전체 End-to-End 테스트 시나리오

1. PowerShell에서 로그인 (`$login`, `$token` 확보)
2. Unity Stop → 콘솔 Clear → Play
3. `[WS] Connected` 로그 확인 (PENDING 재전송 메시지 있으면 리셋하고 1번부터 다시)
4. **Unity 창을 포커스 상태로** 유지 (Editor는 백그라운드에서 Update 느려짐 — `Application.runInBackground = true`로 해결됨)
5. **타이틀 화면 터치** → QR 패널 표시
6. PowerShell에서 세션 생성 (13.5) → 콘솔에 `Recv START_SESSION → Send SESSION_STARTED → ACK` 확인 → 화면 자동 전환
7. **장르 선택 or 직접 입력 → 확인 팝업 → "네!"**
8. Unity 콘솔에 `[API] POST` 로그 확인 → 1~5분 대기
9. `[API] SUCCESS qrPayload=... video=https://...` 뜨면 완료

### 13.9 자주 만나는 에러 & 해결

| 현상 | 원인 | 해결 |
|----|----|----|
| `A002 인증이 필요하거나 만료` | PowerShell 창 닫혀서 `$token` 유실 | 13.4 재실행 |
| `STARTED_BEFORE_CREATED` NACK | `startedAt` 시각이 서버 생성 시각보다 이른 것처럼 보임 (초 단위 정밀도 부족) | `startedAt`을 `yyyy-MM-ddTHH:mm:ss.fff` (밀리초 포함)로 송신 — 현재 구현됨 |
| `CT013 지원하지 않는 장르` | 한글 장르 값 그대로 전송 | 9.4 enum 표에 따라 영문 소문자로 매핑 |
| `E017 이미 완료된 세션` | 동일 `sessionId` 재제출 | 새 세션 생성 or 13.6 리셋 |
| `E020 체험 세션을 시작할 수 없음` | `PENDING`이 아닌 상태에서 `SESSION_STARTED` | 13.6으로 리셋 (새 `startToken` 발급됨) |
| `E023` 세션 완료 불가 현재상태 `PENDING` | `SESSION_STARTED`가 NACK되어 IN_PROGRESS로 못 간 상태 | 상위 NACK 원인 먼저 해결 |
| `C002` 500 Server Error | 서버 내부 오류 | `trackingId` 기록 후 백엔드 담당자에게 공유 |
| Unity가 `START_SESSION` 안 받음 | Editor가 백그라운드라 `DispatchMessageQueue` 안 돌아감 | `Application.runInBackground = true` (구현됨) |
| 명령 쳐도 반응 없음 | 세션은 서버에 생성됐으나 WebSocket 연결 끊김 | Unity 재시작 시 `PENDING` 자동 재전송으로 복구 |
| `WindowsVideoMedia error 0x80004004` / `VideoPlayer cannot play url` | Unity Windows VideoPlayer (WMF)가 특정 HTTPS 스트림 재생 실패 | 영상을 `UnityWebRequest`+`DownloadHandlerFile`로 **로컬 다운로드 후** VideoPlayer에 로컬 경로 전달 (구현됨) |
| RawImage에 영상 안 보임 (`videoDisplayImage 미연결` 경고) | Inspector에서 영상 표시용 RawImage 미연결 | `GameManager` Inspector의 `Video Display Image`에 결과 패널 안의 RawImage 드래그 |
| 영상 자동 재생돼버림 | `Prepare()` 뒤 `Play()` 호출 | `PauseAfterFirstFrame` 코루틴으로 첫 프레임 후 Pause + `time=0` |
| `[GameManager] video URL 비어있음` | 202 응답을 동기 성공으로 처리 (2026-04-26 비동기 전환 미반영) | `APIManager`에서 status 200/202 분기 + `WebSocketClient`에서 `RESULT_READY` 핸들러 추가 (§4.1.1, §12.4 ⑫) |
| 결과 화면이 영원히 안 뜸 (로딩만 계속) | WebSocket 끊겨서 `RESULT_READY` 유실 | §4.3 `GET /status` 폴링 폴백 구현 |
| `409 Conflict` | 이미 결과 제출된 세션에 재제출 | 새 세션 생성 (Dev: 13.6 리셋) |
| `503 Service Unavailable` | AI 백엔드 과부하 | 안내 후 타이틀 복귀, 잠시 후 재체험 |

### 13.10 결과 QR 스캔 시뮬레이션 (방문자 앱 역할)

Unity의 결과 QR 패널에 표시된 QR을 "방문자 앱이 스캔하면 어떻게 되는지" 검증.

```powershell
# Unity 콘솔의 `[API] SUCCESS qrPayload=experience-result:XXX` 값을 복사해서 사용
$qrValue = "experience-result:복사한값"
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/v1/experience/results?qrValue=$qrValue" -Method GET -Headers @{ "Authorization" = "Bearer $token" } | ConvertTo-Json -Depth 5
```

성공 응답에 `boothScore.score=50`, `canViewRanking=true`, `canViewContents=true` 포함되면, 서버 DB에 **방문자 ↔ 체험 영상 연결이 저장**됨. 방문자가 나중에 앱에서 "내 콘텐츠" 조회 시 이 영상이 나타남.

### 13.11 명령어 빠른 참조 (Cheat Sheet)

```powershell
# 1) 로그인
$login = Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/v1/auth/visitor/login" -Method POST -Body '{"visitorCode":"VC-AVAILABLE"}' -ContentType "application/json"
$token = $login.data.accessToken

# 2) 세션 생성 (Unity QR 패널일 때)
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/v1/experience/sessions" -Method POST -Body '{"qrPayload":"experience-start:20"}' -ContentType "application/json" -Headers @{ "Authorization" = "Bearer $token" } | ConvertTo-Json -Depth 5

# 3) 개별 세션 리셋
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/dev/sessions/reset" -Method POST -Body '{"sessionIds":[70XX]}' -ContentType "application/json" | ConvertTo-Json -Depth 5

# 4) 전체 하드코딩 세션 리셋
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/dev/sessions/reset/all" -Method POST | ConvertTo-Json -Depth 5

# 5) 결과 조회 (방문자 앱 역할 확인용)
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/v1/experience/results?qrValue=experience-result:XXX" -Method GET -Headers @{ "Authorization" = "Bearer $token" } | ConvertTo-Json -Depth 5

# 6) 비동기 제출 상태 조회 (WebSocket 폴백, §4.3)
#    $startToken은 WebSocket START_SESSION 메시지의 값. Unity 콘솔 [WS] Recv 로그에서 확인
$sessionId = 70XX
$startToken = "..."
Invoke-RestMethod -Uri "https://dev-api.uiseong.ai.kr/api/v1/experience/sessions/$sessionId/status" -Method GET -Headers @{ "X-Start-Token" = $startToken } | ConvertTo-Json -Depth 5
```

---

## 14. 디스플레이 PC 시스템 (Sub-Display)

부스 화면 외에 **같은 LAN의 별도 PC에서 생성된 영상들을 반복 상영**하는 서브 디스플레이 시스템. AI 무비 디렉터처럼 영상을 생성하는 부스에 유용.

### 14.1 아키텍처

```
┌──────────────────┐      UDP broadcast      ┌──────────────────────┐
│  Booth PC        │  :8090 (255.255.255.255) │  Display PC          │
│  SampleScene     │  ───────────────────▶   │  VideoDisplayScene   │
│                  │                          │                       │
│ DisplayPushSender│  "NEWVIDEO:<videoUrl>"   │  VideoPushReceiver   │
│  (영상 완성 시)  │                          │       ↓               │
│                  │                          │  VideoQueueDisplay   │
│                  │                          │  (큐 5개, 페이드)    │
└──────────────────┘                          └──────────────────────┘
```

### 14.2 동작 사양

- **큐 크기**: 최대 5개. 새 영상 들어오면 맨 앞 삽입, 초과 시 맨 뒤(가장 오래된) 자동 폐기
- **재생 순서**: 큐 맨 앞(최신)부터 순차 → 끝나면 맨 앞으로 루프
- **인터럽트**: 새 영상 도착 시 재생 중이던 영상 즉시 중단 → 페이드 아웃(빠르게) → 새 영상으로 시작
- **전환**: 검정 페이드 인 → 영상 → 검정 페이드 아웃 → 다음 (3단계 A 방식)
- **로컬 다운로드**: 재생 전 mp4를 로컬 임시 폴더로 받아 재생 (Windows WMF HTTPS 이슈 회피)

### 14.3 파일

| 파일 | 위치 | 역할 |
|----|----|----|
| `DisplayPushSender.cs` | 부스 `Scripts/Manager/` | UDP 브로드캐스트 발신 |
| `VideoPushReceiver.cs` | 디스플레이 `Scripts/Display/` | UDP 수신 (별도 스레드 + ConcurrentQueue → Main Thread) |
| `VideoQueueDisplay.cs` | 디스플레이 `Scripts/Display/` | 큐 관리 + 다운로드 + 재생 + 페이드 |

### 14.4 부스 측 세팅

1. `SampleScene`에 빈 GameObject 생성 → 이름 `DisplayPushSender`
2. `DisplayPushSender.cs` 컴포넌트 부착
3. Inspector:
   - `Port`: `8090`
   - `Broadcast Address`: `255.255.255.255` (같은 PC 테스트 시는 `127.0.0.1`)
4. `GameManager.CompleteAndTransitionToResult`의 TransitionTo 콜백 내부에서 `DisplayPushSender.Instance.Push(currentVideoUrl)` 호출 — 결과 패널 뜰 때만 전송 (API 성공 시점 X)

### 14.5 디스플레이 씬 구조 (`VideoDisplayScene`)

```
VideoDisplayScene
├── Main Camera
├── EventSystem
└── Canvas (Scale With Screen Size, 1920x1080)
    │
    ├── VideoDisplay (RawImage, stretch-full)
    │       └── 영상 RenderTexture가 여기에 표시됨 (런타임 자동 할당)
    │
    ├── BlackFade (Image, stretch-full, 검정)
    │       └── Canvas Group (alpha 초기 1, Block Raycasts 해제)
    │
    └── DisplayManager (빈 GameObject)
            ├── VideoPlayer (Play On Awake 해제)
            ├── VideoQueueDisplay (필드 연결)
            │       ├── Video Player ← 같은 오브젝트
            │       ├── Display Image ← VideoDisplay
            │       └── Black Fade ← BlackFade의 CanvasGroup
            └── VideoPushReceiver
                    ├── Queue Display ← 같은 오브젝트의 VideoQueueDisplay
                    └── Port: 8090
```

### 14.6 VideoQueueDisplay Inspector 파라미터

| 필드 | 기본값 | 의미 |
|----|----|----|
| `maxQueueSize` | 5 | 큐 최대 크기 |
| `fadeDuration` | 1.5 | 일반 페이드 시간 (초) |
| `interruptFadeDuration` | 0.4 | 인터럽트 시 빠른 페이드 아웃 |
| `initialRTWidth/Height` | 1920x1080 | 초기 RenderTexture 해상도 (영상 준비 후 실제 해상도로 자동 재생성) |

### 14.7 빌드 구성

Scenes In Build에 두 씬 추가 후, 빌드 대상에 따라 맨 위 순서 교체:

| 빌드 타겟 | 맨 위 씬 |
|----|----|
| 부스용 | `SampleScene` |
| 디스플레이용 | `VideoDisplayScene` |

별도 이름으로 빌드 (예: `BoothClient.exe` / `MovieDisplay.exe`).

### 14.8 네트워크 요구사항

- **같은 서브넷** (Wi-Fi 또는 유선 LAN)
- **UDP 8090 방화벽 허용** (첫 실행 시 Windows 방화벽 팝업 → "허용")
- **공유기를 건너가거나 VLAN 분리된 경우 브로드캐스트 차단** — 이때는 유선 직결 또는 같은 AP

### 14.9 동일 PC 테스트

Unity 에디터(부스) + 빌드(디스플레이)를 같은 PC에서 돌릴 수 있음:
- `DisplayPushSender.broadcastAddress`를 `127.0.0.1`로 임시 변경 (루프백)
- 디스플레이 빌드는 창 모드(`Fullscreen Mode: Windowed`)로 설정해 에디터와 나란히
- 나중에 실기기 연동 시 `255.255.255.255`로 복구

### 14.10 트러블슈팅

| 증상 | 원인 | 해결 |
|----|----|----|
| 디스플레이 빌드가 검정 화면 그대로 | 영상 수신 이벤트 없음 | 부스 콘솔 `[PushSender] 브로드캐스트 전송` 로그 확인. 없으면 `DisplayPushSender` GameObject 누락 |
| 부스는 전송했는데 디스플레이 반응 없음 | 방화벽 또는 다른 서브넷 | Player.log에서 `[PushReceiver]` 로그 확인. 방화벽 UDP 8090 허용 |
| `[PushReceiver] 새 영상 수신` 찍히는데 재생 안 됨 | `VideoPushReceiver.queueDisplay` null | Inspector에서 `Queue Display` 필드 연결 확인 |
| 같은 PC인데 루프백 안 됨 | OS에 따라 `255.255.255.255` 루프 미도달 | `broadcastAddress`를 `127.0.0.1`로 변경 |
| 디스플레이 빌드 실행 즉시 종료 | 포트 충돌 (다른 프로세스가 8090 사용) | 다른 포트 사용 (양쪽 같은 값으로) |

### 14.11 Player.log 위치 (디스플레이 PC 디버깅용)

디스플레이 빌드는 콘솔 창이 없으므로 로그 파일 확인:

```
Windows: %USERPROFILE%\AppData\LocalLow\DefaultCompany\UiseongAIMovieDirector\Player.log
macOS:   ~/Library/Logs/DefaultCompany/UiseongAIMovieDirector/Player.log
```

메모장/텍스트 에디터로 열어 `[PushReceiver]`, `[QueueDisplay]` 로그 검색.

---

## 15. 다른 부스(Content 유형) 개발에 이 가이드 적용하기

이 가이드는 AI 무비 디렉터(Booth 20)를 기준으로 작성됐지만, **동일한 Content 유형의 다른 부스** (스카이 메이커, 이스케이프 룸, 스튜디오, 웰니스 2종, 보이스 시프트, 작가)에도 **95% 그대로 재사용 가능**합니다.

### 15.1 부스 간 공통 부분 (재사용 OK)

- 전체 API 스펙 (인증, WebSocket, REST) — §1~§8
- WebSocket 프로토콜 전체 — §3
- 공통 응답/에러 구조 — §6~§7
- Unity 스크립트 전체 (`WebSocketClient`, `APIManager`, `QRGenerator`, `GameManager` 뼈대)
- 디스플레이 PC 시스템 — §14 (영상 생성 부스에 공통)
- UX 컴포넌트들 (`BouncingText`, 로딩바 3단계, 결과 QR 팝업, 재생 컨트롤) — §12.5

### 15.2 부스별 바꿔야 하는 부분

**① 부스 고유 식별자** (§9.1 매핑 표 참조)

| 필드 | 위치 | 예시 (AI 스카이 메이커) |
|----|----|----|
| Booth ID | WebSocketClient Inspector | `8` |
| Start QR 값 | GameManager (`QRGenerator.ShowQR`) | `experience-start:8` |
| Booth Secret | WebSocketClient Inspector | `bsk_bWUcrEGcYXit1nHW` |

**② 결과 제출 요청 필드** (§9.2 유형별 표 참조)

| 부스 | 요청 필드 |
|----|----|
| AI 무비 디렉터 | `director`, `genre`, `prompt` |
| AI 스카이 메이커 | `temperature`, `humidity`, `pressure`, `windSpeed`, `cloudCover`, `precipitation` |
| AI 이스케이프 룸 | `originalImage`(File), `targetImage`(File) |
| AI 스튜디오 | `theme`, `prompt` |

**③ 입력 UI 및 유효성 검증** (부스 기획서에 따름)

무비: 장르 버튼 + 텍스트 입력
스카이: 슬라이더/다이얼로 기후값 (숫자 6개)
이스케이프: 사진 2장 촬영/선택 (File 업로드, `MultipartFormFileSection`)
스튜디오: 테마 선택 + 프롬프트 입력

**④ APIManager의 Submit 메서드 시그니처**

예: 스카이 메이커용

```csharp
public void SubmitSkyMakerResult(int sessionId, string startToken,
    float temperature, float humidity, float pressure,
    float windSpeed, float cloudCover, float precipitation)
{
    var form = new List<IMultipartFormSection>
    {
        new MultipartFormDataSection("temperature", temperature.ToString()),
        new MultipartFormDataSection("humidity", humidity.ToString()),
        // ...
    };
    // 나머지는 무비 디렉터와 동일
}
```

**⑤ 응답의 `contents` 키 이름 및 응답 코드**

| 부스 | 유형 | 결과 제출 응답 | contents 키 | 비고 |
|----|----|----|----|----|
| 무비 디렉터 | Content | **202 → WS `RESULT_READY`** | `GENERATED_VIDEO` | 영상 URL |
| 스카이 메이커 | Content | **202 → WS `RESULT_READY`** | `GENERATED_VIDEO` | 하늘 영상 URL |
| 이스케이프 룸 | Content | **202 → WS `RESULT_READY`** | `DEEPFAKE_IMAGE` (추정) | 이미지 URL |
| 스튜디오 | Content | **202 → WS `RESULT_READY`** | `GENERATED_AUDIO` (추정) | 음악 URL |
| 작가 | Content | **202 → WS `RESULT_READY`** | 미확정 | 네컷 만화 이미지 |
| 보이스 시프트 | Content | **202 → WS `RESULT_READY`** | 미확정 | 변환 음성 URL |
| 웰니스 (신체균형/스트레스) | Content | **202 → WS `RESULT_READY`** | 미확정 | 분석 결과 |
| 탐정 | Score+Content | **202 → WS `RESULT_READY`** | 미확정 | 명화 합성 이미지 |
| 블록 크리에이터 | Score Variable | **202 → WS `RESULT_READY`** | 미확정 | 비주얼 쿼리 결과 |
| 보물 사냥꾼/오토 드라이브/메디컬/드로잉/아카이브 | Score Fixed | **200 (동기)** | `{}` (생성물 없음) | 점수만 |
| 오디세이/강 건너기/아바타 | Score Variable | **200 (동기)** | `{}` (생성물 없음) | 점수만 |

정확한 키는 **Swagger UI에서 각 부스의 result 응답 스키마**로 확인.

**핵심**: AI 생성형 부스(202 그룹)는 모두 동일한 비동기 패턴 사용 — 클라이언트 코드의 `APIManager` 분기 + `WebSocketClient` `RESULT_READY` 핸들러는 **모든 Content/AI 부스에서 그대로 재사용** 가능.

**⑥ 생성물 표시 UI**

- 영상 부스 (무비, 스카이): VideoPlayer + RawImage + RenderTexture (이 가이드와 동일)
- 이미지 부스 (이스케이프, 탐정, 웰니스): RawImage + `UnityWebRequestTexture.GetTexture`로 다운로드 후 Texture2D 할당
- 음악 부스 (스튜디오): AudioSource + `UnityWebRequest`로 다운로드 후 AudioClip 할당
- 네컷 만화 (작가): 이미지 여러 장 — Layout Group으로 배치

### 15.3 새 부스 개발 권장 순서

1. **문서 체크**: Swagger UI에서 해당 부스의 **요청 필드/응답 스키마/응답 코드(200 vs 202)** 확인
2. **§9.1, §9.2, §15.2 표**에서 Booth ID, Secret, QR, 요청 필드, contents 키, 응답 코드 복사
3. **기존 AI 무비 디렉터 프로젝트 복제**해서 새 이름으로 생성
4. **`WebSocketClient` Inspector**: Booth ID, Booth Secret 교체
5. **`QRGenerator.ShowQR`의 값**: `experience-start:{새 ID}`로 교체
6. **`APIManager.SubmitMovieResult` → 새 부스용 Submit 메서드** 새로 작성 (이름/필드만 변경). **응답 코드 분기 200/202/409/503 로직은 그대로 재사용**
7. **입력 UI 재설계** (부스 기획서 기준) → GameManager에 연결
8. **응답 DTO 클래스**의 `contents` 필드명 교체 (예: `GENERATED_VIDEO` → `GENERATED_AUDIO`)
9. **결과 화면의 생성물 표시 UI** 부스 타입에 맞춰 교체 (영상/이미지/음악)
10. **WebSocket `RESULT_READY` 핸들러**의 영상→이미지/음악 표시 로직 교체 (Content 유형 부스에 한함). `APIManager.ResultContents` 필드명만 부스에 맞게 바꾸면 나머지 흐름은 동일
11. **§13 개발 테스트 절차**로 PowerShell 시뮬레이션 (세션 생성 URL의 `experience-start:20`만 새 ID로 교체). 202 응답 확인 → WS `RESULT_READY` 수신 확인
12. **§14 디스플레이 PC 시스템**은 영상 생성 부스에만 재사용 (이미지/음악 부스는 별도 설계 필요)

### 15.4 Score Variable/Fixed 부스 개발 시 차이점

이 가이드는 Content(50점 고정) 기준이라 **점수 전송이 없음**. Score Variable(오디세이, 강 건너기 등) 부스는 `aqScore` 필드 추가 필요:

```csharp
form.Add(new MultipartFormDataSection("aqScore", finalScore.ToString()));
```

Score Fixed(보물 사냥꾼, 메디컬 센터 등)는 요청 body 완전 비어있음 (`X-Start-Token` 헤더만).

Score + Content(탐정)는 `aqScore` + 파일 둘 다:
```csharp
form.Add(new MultipartFormDataSection("aqScore", score.ToString()));
form.Add(new MultipartFormFileSection("originalImage", imageBytes, "photo.jpg", "image/jpeg"));
```

### 15.5 Participation 부스 (QR 스캔 전용)

참여형 부스(인식 원리, 오목 대결, 시력건강, 팩토리)는 **Unity 프로젝트 없음** — 물리 QR 스티커만 부착. 방문자 앱이 `participation:{boothId}` 스캔하면 서버가 자동 50점 기록. 개발 작업 불필요.

---

## 16. 업데이트 이력

| 날짜 | 변경 내용 |
|----|----|
| 2026-03-14 | 최초 작성 |
| 2026-03-18 | Ranking API 추가 |
| 2026-03-22 | Experience API 추가 (결과 제출/조회) |
| 2026-03-25 | Experience API 추가 (세션 등록) |
| 2026-04-01 | Auth/Visitor/Content API 추가, 에러 코드 전체 업데이트, 결과 조회 경로 수정 |
| 2026-04-17 | **WebSocket 프로토콜 도입** (부팅 시 상시 연결, `startToken` 체계), 결과 제출 헤더 `X-Start-Token` 사용, 응답 구조 `result` 래핑 (`score`/`contents`/`analysis`), Facility Breaking Change 반영, Dev 리셋 API 응답 구조 업데이트, visitorCode 기반 JWT 인증 체계 반영 |
| 2026-04-20 | **Unity 구현 및 테스트 반영**: 무비 디렉터 `genre` enum (§9.4), 개발 테스트 절차 섹션 (§13) 추가 (PowerShell 시뮬레이션, 세션 리셋, 자주 만나는 에러, Cheat Sheet). `startedAt` 밀리초 정밀도로 STARTED_BEFORE_CREATED 회피. 구현 체크리스트 현 상태 반영 |
| 2026-04-20 (2차) | **End-to-End 완료**: §12 전면 재작성 (씬 구조 10개 패널, 구현 완료 체크리스트, 구현 결정·함정 11가지). 추가된 기능: 로컬 영상 다운로드(Windows HTTPS 회피), 동적 RenderTexture, 첫 프레임 일시정지, 재생 진행바+시간 텍스트, 3단계 로딩바(선형→creep→완료 연출), `BouncingText` UI 컴포넌트, 모든 홈 버튼 일원화(`ResetToTitle`). QR 스캔 시뮬레이션으로 결과 저장 검증까지 완료 |
| 2026-04-20 (3차) | **완전 레퍼런스화**: §12.5 UX 보조 컴포넌트 (BouncingText, 결과 제목 `BuildResultTitle`, 재생 컨트롤 UI), §12.6 로딩 중 미니게임(RunnerGame, 러너 게임: 점프+동전+장애물+시간 가속+관대한 히트박스). **§14 디스플레이 PC 시스템 신규** (UDP 브로드캐스트 + 큐 5개 + 페이드 재생, 부스/디스플레이 빌드 분리). **§15 다른 부스(Content 유형) 개발 적용 가이드** — 공통 vs 부스별 차이, 권장 개발 순서, Score 유형별 차이, Participation 부스 안내. 로딩바 단순화 (0→99% 선형, 99에서 정지, 응답 시 99→100) |
| 2026-04-24 | **WebSocket `SESSION_ABORT` 메시지 추가** (디바이스 idle timeout 세션 중단). §3.4에 메시지 스펙, NACK `INVALID_STATE` 의미 보강. |
| 2026-04-26 | **🔴 비동기 결과 제출 전환 (BREAKING)** — AI 처리 필요한 Content 유형 부스 8종은 `POST /result`가 즉시 `202 Accepted` 반환하고 응답 본문에 영상 URL 없음. 결과는 WebSocket `RESULT_READY` / `RESULT_FAILED` 메시지로 비동기 전달. `GET /api/v1/experience/sessions/{id}/status` 폴백 엔드포인트 추가. 응답 코드 `409 Conflict`/`503 Service Unavailable` 추가. §3.3, §4.1, §4.3 신규/재작성. |
| 2026-05-12 | **Unity 클라이언트 비동기 패턴 반영** — `APIManager`에 status code 분기(200/202/409/503), `WebSocketClient`에 `RESULT_READY`/`RESULT_FAILED` 핸들러 + 이벤트, `GameManager`가 WS 결과 이벤트 구독으로 변경. 함정 ⑫번(202 처리), §13.7/§13.9 비동기 로그 예시, §15.2 부스별 응답 코드 표 추가. |
