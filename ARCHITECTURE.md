# CutPigPoint — Architecture

App tính điểm + chơi online Tiến Lên Miền Nam (2-4 người) cho một nhóm bạn. Có 2 chế độ song song:
- **Chấm điểm thủ công** (offline, người chơi ngồi cùng bàn vật lý): màn `Ván chơi`/`Ván mới`/`Người chơi`.
- **Chơi online realtime** (Phase 2-4): màn `Phòng online` — lobby SignalR + gameplay TLMN có engine luật server-side.

Stack: .NET 6 Web API + SignalR + React (Vite) + PostgreSQL. Deploy: Railway (API + Postgres) + Vercel (frontend).

Luật chơi chi tiết: xem [RULE.md](RULE.md).

## Layout

```
/                       repo root
├── CutPig/             ASP.NET Core 6 Web API (server)
│   ├── Program.cs                bootstrap + DI + DB init + map hub
│   ├── Controllers/              PlayersController, GamesController,
│   │                             AuthController, AdminUsersController,
│   │                             RoomsController
│   ├── Hubs/RoomHub.cs           SignalR hub cho phòng online + gameplay
│   ├── Middleware/AuthMiddleware.cs  Bearer auth cho /api/* (trừ login + /hubs/*)
│   ├── Services/                 TienLenScoringService (legacy manual),
│   │                             Bida9BallScoringService (legacy),
│   │                             PasswordHasher,
│   │                             MatchManager (in-memory active matches),
│   │                             MatchTimerService (HostedService auto-pass + auto-next-round),
│   │                             RoomPresenceTracker (connId↔user↔room)
│   ├── Game/                     Card engine TLMN
│   │   ├── Card.cs               Rank/Suit, Deck shuffle
│   │   ├── TienLenCombo.cs       Detect combo + Beats + DetectWhiteWin
│   │   └── Match.cs              Match/MatchPlayer state types (in-memory only)
│   ├── Domain/                   EF entities: Player, Game, GamePlayer, GameRound,
│   │                             RoundResult, AppUser, AuthToken, Room, RoomSeat
│   ├── Data/AppDbContext.cs      EF Core + Npgsql
│   └── Dtos/Dtos.cs              C# records cho API + hub events
├── client/             React + Vite + TypeScript
│   └── src/
│       ├── App.tsx               router + auth gate + nav (admin link conditional)
│       ├── api.ts                fetch wrapper + types + room/match types
│       ├── auth/AuthContext.tsx  token + userId + isAdmin + displayName
│       ├── hooks/
│       │   └── useRoomConnection.ts  SignalR connect, room+match state, hand
│       ├── pages/
│       │   ├── GamesPage, NewGamePage, GamePlayPage, PlayersPage  (legacy manual scoring)
│       │   ├── LoginPage, ProfilePage, AdminUsersPage              (auth/account)
│       │   ├── RoomsPage, RoomLobbyPage, RoomPlayPage              (online play)
│       │   └── DemoPage                                            (static visual prototype)
│       ├── game/                 Tết-themed UI primitives
│       │   ├── cards.ts          types + detectCombo + comboBeats (mirror server)
│       │   ├── CardSvg.tsx       lá bài SVG (face + back hoa mai)
│       │   ├── Hand.tsx, Seat.tsx, Table.tsx, PlayArea.tsx
│       │   ├── effects/          MaiBranch, Confetti
│       │   └── demo.css          design tokens (palette Tết, wood, gold)
│       └── ui/                   Avatar, Icon, Toast, helpers, image
├── Dockerfile          multi-stage build cho server
├── railway.toml        Railway config
├── README.md
├── ARCHITECTURE.md     (this file)
└── RULE.md             chi tiết luật TLMN
```

## Server (CutPig/)

- **Framework**: ASP.NET Core 6, EF Core 6, Npgsql, SignalR (đi kèm Sdk.Web, không cần NuGet riêng).
- **DI**:
  - `AppDbContext` (Scoped, Npgsql)
  - `TienLenScoringService`, `Bida9BallScoringService` (Scoped, legacy manual scoring)
  - `RoomPresenceTracker` (Singleton) — in-memory map connectionId ↔ (userId, roomId), userId ↔ connectionIds (cho private send).
  - `MatchManager` (Singleton) — in-memory active matches; lock per-room cho deal/play/pass.
  - `MatchTimerService` (HostedService) — loop 1s: auto-pass khi `TurnDeadline` qua + auto-start next round khi `NextRoundAt` qua.
- **Startup** ([Program.cs](CutPig/Program.cs)):
  - Bind `0.0.0.0:$PORT` (default 8080) — bắt buộc cho Railway.
  - `ResolveConnectionString` đọc `DATABASE_URL` (Heroku/Railway URL) hoặc `ConnectionStrings:DefaultConnection`.
  - CORS policy `AllowFrontend`: origin từ env `FRONTEND_ORIGIN` + config + `localhost:5173/3000` mặc định. **`.AllowCredentials()`** bắt buộc cho SignalR.
  - DB init dùng `EnsureCreated()` + chuỗi `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` để migrate idempotent (không dùng EF Migrations). Tạo `AppUsers`, `AuthTokens`, `Rooms`, `RoomSeats` qua `CREATE TABLE IF NOT EXISTS` (vì `EnsureCreated` skip nếu DB đã có table).
  - **Bootstrap admin**: nếu `AppUsers` trống → tạo user từ env `INITIAL_USERNAME`/`INITIAL_PASSWORD` (default `admin`/`admin`) với `IsAdmin=true`. Nếu có user nhưng không ai admin → promote user cũ nhất (migration safety).
  - Endpoints health: `GET /` trả "running"; `GET /health` check DB connect.
  - Map hub: `app.MapHub<RoomHub>("/hubs/room")`.
- **Routes**:
  - `api/auth` — `POST /login`, `POST /logout`, `GET /me`, `POST /change-password`. Login public; còn lại cần Bearer.
  - `api/admin/users` — list/create/update/delete user (admin only). Tạo user xong admin đưa username/password cho người chơi.
  - `api/players` — CRUD player + avatar (legacy manual scoring; chưa hợp nhất với AppUser).
  - `api/games` — list/get/create/finish/add-round/delete-round/delete-game (legacy manual scoring).
  - `api/rooms` — list (waiting; admin thấy all), create (host auto-seat 0), get by code, delete (host khi Waiting, admin bất kỳ status).
  - `/hubs/room` — SignalR endpoint (chi tiết bên dưới).
- **Auth** ([CutPig/Middleware/AuthMiddleware.cs](CutPig/Middleware/AuthMiddleware.cs)):
  - Mọi `/api/*` (trừ `/api/auth/login` và `/hubs/*`) yêu cầu `Authorization: Bearer <token>`.
  - Token random 32 byte base64url, lifetime 4h, lưu `AuthTokens`. Hash PBKDF2-SHA256 100k iter ([Services/PasswordHasher.cs](CutPig/Services/PasswordHasher.cs)).
  - Middleware set `HttpContext.Items["UserId" | "Username" | "DisplayName" | "IsAdmin"]` để controllers đọc.
  - SignalR auth: token gửi qua query string `?access_token=...` (vì WebSocket browser không tiện gắn header). `RoomHub.AuthenticateAsync()` đọc từ `Context.GetHttpContext()?.Request.Query`.

## Domain model

### Legacy (manual scoring)
```
Player (Id, Name, Nickname?, AvatarData? string base64-data-url, CreatedAt)
Game (Id, Type=TienLenMienNam, StartedAt, FinishedAt?, Players: GamePlayer[], Rounds: GameRound[])
GamePlayer (Id, GameId, PlayerId, Seat 1..4)  -- unique (GameId, Seat)
GameRound (Id, GameId, RoundNumber, ManualScoring bool, Results: RoundResult[]) -- unique (GameId, RoundNumber)
RoundResult (Id, GameRoundId, PlayerId, Rank?, ...inputs..., Score)
GameType enum: TienLenMienNam=1, Bida9Ball=2, BidaDen=3
```

### Auth / Account
```
AppUser (Id, Username unique, PasswordHash, DisplayName, AvatarData?, IsAdmin, CreatedAt, UpdatedAt)
AuthToken (Id, Token unique, UserId FK→AppUser cascade, CreatedAt, ExpiresAt)
```

### Phòng online
```
Room (Id, Code 6char unique, HostUserId FK→AppUser restrict, GameType=1, MaxSeats 2..4,
      Status: Waiting=0/Playing=1/Finished=2, CreatedAt, StartedAt?, FinishedAt?,
      ShowOpponentCardCount (host toggle, chỉ chỉnh khi Waiting; copy vào Match lúc StartGame),
      Seats: RoomSeat[])
RoomSeat (Id, RoomId FK→Room cascade, SeatIndex 0..MaxSeats-1, UserId FK→AppUser restrict, JoinedAt)
  -- unique (RoomId, SeatIndex) và unique (RoomId, UserId)
```

Phòng persist trong DB nhưng **match state in-memory** (trong `MatchManager`) — restart server giữa ván = mất ván.

## SignalR — `/hubs/room`

Auth: `?access_token=<bearer>`. Sau khi connect, client gọi `JoinRoom(code)` để vào group `room:{id}` và bắt đầu nhận events.

### Hub methods (client → server)
| Method | Mô tả |
|---|---|
| `JoinRoom(code)` | Vào group + trả `RoomStateDto`; nếu match đang chạy: gửi private hand + `MatchState` cho caller. |
| `TakeSeat(seatIndex)` | Ngồi vào ghế (chỉ khi `Waiting`); broadcast `RoomState`. |
| `LeaveSeat()` | Rời ghế (chỉ khi `Waiting`). |
| `StartGame()` | Host start: đổi Room.Status=Playing, tạo Match, deal 13 lá/người, detect về trắng → broadcast `MatchState` + `PrivateHand`; nếu về trắng → emit `RoundEnd` ngay. |
| `PlayCards(List<CardDto>)` | Đánh bài: validate combo + chặn được + đúng lượt + chưa pass (trừ 4 đôi thông). Broadcast `MatchState`, resend `PrivateHand` cho người đánh; nếu round end → `RoundEnd`. |
| `PassTurn()` | Bỏ lượt trick hiện tại (không bỏ được khi mở nước). Khi tất cả người khác pass → reset trick → turn về owner. |
| `StartNextRound()` | Host (hoặc system auto) deal ván tiếp. |
| `EndMatch()` | Host kết thúc trận → emit `MatchEnd` với bảng tổng điểm. |
| `RequestMatchState()` | Reconnect/refresh: gửi lại state + private hand. |

### Server → client events
| Event | Payload | Khi nào |
|---|---|---|
| `RoomState` | `RoomStateDto` | Mỗi khi seat/online thay đổi. |
| `GameStarted` | `Guid roomId` | Host bấm Start → client redirect `/play/:code`. |
| `MatchState` | `MatchPublicStateDto` | Mỗi play/pass/round start. Chứa `roundNumber`, `currentTurnSeatIndex`, `currentTrick`, `turnDeadline`, `nextRoundAt`, `hostUserId`, players info (cards left, finalRank, passedThisTrick, totalScore, whiteWinReason). |
| `PrivateHand` | `PrivateHandDto` (matchId, hand) | Send chỉ tới connections của user đó qua `Clients.Clients(connIds)`. |
| `RoundEnd` | `RoundEndDto` (roundNumber, wasWhiteWin, results[]) | Hết ván (có cả tổng điểm dồn). |
| `RoundHistory` | `RoundHistoryDto` (matchId, rounds[]) | Khi `JoinRoom`/`RequestMatchState`: gửi snapshot tất cả ván đã kết thúc trong trận hiện tại (in-memory). |
| `MatchEnd` | `MatchEndDto` (finalScores[]) | Host kết thúc trận → đóng phòng. |

### Auto behaviors (`MatchTimerService`)
- Mỗi 1 giây, scan:
  - **Active matches có `TurnDeadline < now`** → gọi `Pass(... isAutoPass: true)`. Nếu mở nước (không trick) → auto đánh lá nhỏ nhất.
  - **Matches `WaitingNextRound` có `NextRoundAt < now`** → gọi `StartNextRound(... null)` (system trigger) → deal lại + broadcast `MatchState` + `PrivateHand`.
- `NextRoundAt` set = `now + 20s` mỗi khi round chuyển sang `WaitingNextRound` (host có thể gọi `StartNextRound` để skip countdown).

## Card engine TLMN (`CutPig/Game/`)

- **`Card`** = `(int Rank 3..15, Suit Spades<Clubs<Diamonds<Hearts)`. Rank 15 = "2".
- **`Deck.Build()`** + **`Shuffle(rng)`** — 52 lá.
- **`TienLenComboEngine`**:
  - `Detect(cards)` → `Combo(Kind, Cards, TopValue)`. Kind: Single, Pair, Triple, Four, Run (≥3 liên tiếp, không 2), RunOfPairs (≥6, không 2).
  - `Beats(current, next)`: cùng kind + length + topValue cao hơn. **Cộng thêm** chặt heo:
    - 4 đôi thông (RunOfPairs len=8) chặt mọi thứ.
    - Tứ quý chặt 1 con 2, đôi 2, 3 đôi thông.
    - 3 đôi thông (RunOfPairs len=6) chặt 1 con 2.
  - `IsFourPairRun(combo)` — exempt từ pass-tracking.
  - `DetectWhiteWin(hand)` → string reason hoặc null. Check: sảnh 3-A (12 lá), tứ quý 2, 6 đôi, 5 đôi thông.

## MatchManager flow

`Match` (in-memory) chứa: Players (gồm Hand, FinalRank, TotalScore cộng dồn, PassedThisTrick, WhiteWinReason), CurrentTurnSeatIndex, CurrentTrick + CurrentTrickOwnerId, Status (InProgress/WaitingNextRound/Finished), RoundNumber, PreviousRoundWinnerId, TurnDeadline, NextRoundAt.

- **`Create(roomId, hostUserId, players)`**: deal lần 1 (13 lá/người, dư úp), detect về trắng; nếu có ai → status `WhiteWinChoice` + `WhiteWinDeadline = now + 10s` (mỗi candidate Accept/Decline). Nếu không: chọn người đi đầu (giữ 3♠, fallback seat 0).
- **`StartNextRound(roomId, hostUserId?)`**: tham số host nullable để cho phép timer service (system) trigger. Reset hand + flag, deal lại; người đi đầu = winner ván trước (PreviousRoundWinnerId).
- **`RespondWhiteWin(userId, accept)`**: candidate chọn Có/Không trong phase `WhiteWinChoice`. Khi tất cả candidate đã chọn (hoặc timeout 10s qua `ResolveWhiteWinTimeout`): nếu **không ai accept** → clear WhiteWinReason, ván chơi bình thường; nếu **có** → end round với candidates accepted thắng.
- **`Play(roomId, userId, cards)`**: validate (có trong tay, combo hợp lệ, chặn được, mở nước ván 1 chứa 3♠ **nếu 3♠ trong tay ai đó**), apply, clear PassedThisTrick nếu đánh 4 đôi thông, check round end (≤1 người còn bài) → set WaitingNextRound + NextRoundAt.
- **`Pass(roomId, userId, isAutoPass=false)`**: set PassedThisTrick. Khi tất cả người khác đều pass: nếu ai còn 4 đôi thông trong tay → status `PendingTrickCut` + 5s window (xem `CutNewTrick` / `DeclineTrickCut`); nếu không → reset trick, turn về owner. Auto-pass khi mở nước = đánh lá nhỏ nhất.
- **`CutNewTrick(userId, cards)`**: trong phase `PendingTrickCut`, player có 4 đôi thông đánh ra để chặn người sắp mở trick mới, giành lượt cho mình.
- **`ComputeRoundScores(match)`**: white-win zero-sum multi-winner — mỗi loser **-2 × số winner**, mỗi winner **+2 × số loser** (ví dụ 4 người 1 trắng = trắng +6, mỗi người kia -2; 4 người 2 trắng = mỗi trắng +4, mỗi thua -4). Bình thường theo table rank: 4 người ±2/±1, 3 người +2/0/-2, 2 người +1/-1, **cộng thêm `RoundChopExtra[playerId]`** (chop-pig settlements tích lại từ các trick trong ván).
- **Chop-pig chain**: `Match.TrickChopChain` track sequence (playerId, chopValue) cho trick hiện tại. Mỗi `Play` / `CutNewTrick` / auto-pass play smallest → push entry nếu `ChopValue(combo) > 0` (heo, 3-đôi-thông, tứ quý, 4-đôi-thông). Khi trick reset (allOthersPassed → reset, hoặc decline trick-cut, hoặc round end) → `SettleTrickChopChain`: nếu chain.Count ≥ 2, last cutter +sum(chain[0..^1]), second-to-last -sum. Pot dồn vào `RoundChopExtra` để cộng vào điểm ván.
- **3♠ cuối**: `MatchPlayer.FinishedWithThreeOfSpades` set khi nước cuối đánh ra là **combo single đúng 1 lá `3♠`** (`cards.Count == 1 && cards[0] == 3♠`). Sảnh/đôi/sám chứa `3♠` không tính. `MatchPlayer.StuckWithThreeOfSpades` set khi round end mà player còn **duy nhất 1 lá** trong tay là `3♠` (đui đúng nghĩa, không chỉ "có 3♠ giữa nhiều lá khác"). `ComputeRoundScores` áp: Nhất + cờ Finished → +(n-1) / others -1 (zero-sum); Chót + cờ Stuck → -3 / others +1 (không zero-sum khi <4 người). Cả 2 không áp khi white-win.
- **Phán xử (Judge)**: `Match.JudgeTriggered`. Track `MatchPlayer.HasPlayedThisRound` (set true sau mỗi `Play`/`CutNewTrick`/auto-pass). Khi player vừa về Nhất, `CheckAndApplyJudge` scan: ai chưa ra bài = victim (`JudgeIsVictim` + `JudgeHeldValue = ComputeHeldValue(hand)`); ai đã ra bài (không phải Nhất) = pardoned. **Case A** (0 pardoned) / **Case B** (1 pardoned): kết thúc ván ngay, gán FinalRank. **Case C** (≥2 pardoned): victim gán FinalRank=n, pardoned tiếp tục chơi xác định Nhì/Ba. `ComputeJudgeScores`: mỗi victim -(4+held), Nhất +∑; Case B pardoned -1 cho Nhất; Case C sub-rank +1/-1 hoặc +2/0/-2 + chop-pig giữa pardoned. **Thay thế toàn bộ** scoring (bỏ base rank, chop-pig của winner, đui 3♠). **Ngoại lệ**: nếu winner về bằng `3♠` cuối (`FinishedWithThreeOfSpades`), cộng thêm bonus 3♠ (+(n-1) / -1) lên trên judge formula.

## Avatar

- Lưu base64 data URL trong cột `Players.AvatarData` (string). Limit 200 KB sau decode. Whitelist content-type `image/jpeg|png|webp`.
- Frontend ([client/src/ui/image.ts](client/src/ui/image.ts)) resize về 256×256 JPEG trước khi upload.
- `GET /api/players/{id}/avatar` decode → trả binary với `Cache-Control: public, max-age=31536000, immutable`. Cache-bust ở client bằng query `?v=<timestamp>`.
- **AppUser** đã có cột `AvatarData` nhưng chưa wire endpoint upload — Phase tiếp.

## Frontend (client/)

- **Stack**: React 18, react-router-dom v6, Vite, TypeScript, framer-motion, clsx, @microsoft/signalr.
- **API base**: `import.meta.env.VITE_API_BASE` + `/api`. SignalR hub = `${VITE_API_BASE}/hubs/room?access_token=...`.
- **Auth**: token `localStorage[cutpig.auth.token]`. AuthContext giữ `userId`, `username`, `displayName`, `isAdmin`. Mỗi request fetch tự gắn Bearer; 401 → clear + về LoginPage.
- **Routes** ([App.tsx](client/src/App.tsx)):
  - Public: `/demo` (prototype visual, bypass auth).
  - Auth gate trước các route sau:
    - `/` GamesPage (legacy), `/players`, `/new`, `/games/:id` (legacy manual scoring).
    - `/rooms` RoomsPage, `/rooms/:code` RoomLobbyPage, `/play/:id` (= code) RoomPlayPage.
    - `/profile` ProfilePage.
    - `/admin/users` AdminUsersPage (chỉ render khi `state.isAdmin`).
  - Nav: admin-only link "Quản lý user"; "Phòng online" icon `globe` (phân biệt với "Ván chơi" icon `cards`).
- **`useRoomConnection(code)`** ([client/src/hooks/useRoomConnection.ts](client/src/hooks/useRoomConnection.ts)):
  - Quản lý `HubConnection`, auto-reconnect ([0, 2000, 5000, 10000] ms).
  - State: `status`, `state` (RoomState), `matchState`, `privateHand`, `roundEnd`, `matchEnd`, `error`.
  - Methods: `takeSeat`, `leaveSeat`, `startGame`, `startNextRound`, `endMatch`, `playCards`, `passTurn`, `requestMatchState`, `clearRoundEnd`.
  - Listen events: `RoomState`, `PrivateHand`, `MatchState`, `RoundEnd`, `MatchEnd`, `GameStarted`.
- **RoomPlayPage**:
  - Bàn Tết với 2-4 seat. Seat **của mình ở dưới** (rotate `SEAT_POSITIONS` theo `me.seatIndex`).
  - Hand fan **responsive**: đo `handAreaRef.offsetWidth`, tính spread = `(available - cardWidth) / (count-1)` clamp giữa min/max. Card size `sm` (44×64) khi viewport < 720px, ngược lại `md` (64×92).
  - Click chọn lá → button "Đánh (N)" enabled nếu `detectCombo` ra hợp lệ + chặn được trick (hoặc 4 đôi thông + đã pass).
  - Visual: seat hiện cờ "BỎ LƯỢT", badge "CHỦ", rank-tag-mini khi finish, total score cộng dồn.
  - Modal `roundEnd`: hiện rank + điểm ván + tổng dồn + countdown "Ván tiếp sau Xs". Host thấy thêm nút "Kết thúc trận".
  - Modal `matchEnd`: bảng final xếp theo total score giảm dần.
- **Mobile (< 720px)**:
  - Bàn aspect-ratio 1:1, seat compact (avatar 28px, font 11px), left/right seat `top: 60px` (không đè giữa).
  - Hand fan thấp hơn (80px), spread 14-24px, card sm.

## Deploy

- **Server (Railway)**: Dockerfile multi-stage (sdk:6.0 build → aspnet:6.0 runtime). Railway tiêm `PORT`, `DATABASE_URL`. Đặt `FRONTEND_ORIGIN=https://<vercel-domain>` để CORS pass. Healthcheck `/`. Optional: `INITIAL_USERNAME`/`INITIAL_PASSWORD` cho lần bootstrap đầu. Gói Hobby $5/tháng vừa đủ cho nhóm bạn (≤ vài chục user, vài trăm ván/tháng).
- **Frontend (Vercel)**: root `client/`. `VITE_API_BASE=https://<railway-domain>` (no trailing slash). Build = `npm run build`, output `dist/`. Hobby plan đủ.

## Quy ước

- **Tiếng Việt** cho mọi text user-facing (toast, label, error message, UI strings).
- **DB schema**: thêm field mới vào entity → phải thêm `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` trong `Program.cs` (DB cũ trên Railway không tự migrate qua `EnsureCreated`). Bảng mới → `CREATE TABLE IF NOT EXISTS` + `CREATE INDEX IF NOT EXISTS`.
- **DTO sync**: C# record trong `CutPig/Dtos/Dtos.cs` ↔ TS interface trong `client/src/api.ts` — sync tay (DTO mới phải copy 2 chỗ).
- **Card combo logic** ở 2 nơi: server [TienLenCombo.cs](CutPig/Game/TienLenCombo.cs) (source of truth, validate); client [cards.ts](client/src/game/cards.ts) (mirror, UX validate trước khi gửi). **Đổi rule chặn = phải sửa cả 2.**
- **Match state in-memory**: hiện chỉ `MatchManager` (Singleton). Không persist DB → restart Railway giữa ván = mất ván. Phase tiếp có thể move sang DB hoặc Redis.
- **SignalR auth**: query string `?access_token=...` (không header). `AuthMiddleware` skip `/hubs/*`; Hub tự validate qua `AuthenticateAsync`.
- **Game type Bida9Ball / BidaDen**: chừa enum, có scoring service, nhưng phòng online (`/rooms`) chỉ hỗ trợ TLMN — Phase tiếp mở rộng.

## Lộ trình tương lai

- Persist match state vào DB hoặc Redis để survive restart.
- Chat trong phòng.
- Spectator mode (xem không ngồi).
- Replay ván cũ.
- Leaderboard tổng (tổng điểm dồn qua nhiều trận).
- Mở rộng `/rooms` cho Bida 9 Ball / Bida đền online.
- Wire avatar cho `AppUser` (hiện chỉ schema, chưa endpoint).
