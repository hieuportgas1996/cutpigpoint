# CutPigPoint — Architecture

App tính điểm Tiến Lên Miền Nam (4 người) cho một nhóm bạn. Stack: .NET 6 Web API + React (Vite) + PostgreSQL. Deploy: Railway (API + Postgres) + Vercel (frontend).

## Layout

```
/                       repo root
├── CutPig/             ASP.NET Core 6 Web API (server)
│   ├── Program.cs
│   ├── Controllers/    PlayersController, GamesController
│   ├── Services/       TienLenScoringService  ← logic tính điểm
│   ├── Domain/         Player, Game, GamePlayer, GameRound, RoundResult, GameType
│   ├── Data/           AppDbContext (EF Core + Npgsql)
│   └── Dtos/           Dtos.cs (records)
├── client/             React + Vite + TypeScript (frontend)
│   └── src/
│       ├── App.tsx     router, 4 routes
│       ├── api.ts      fetch wrapper + types (mirror DTOs server)
│       ├── pages/      GamesPage, NewGamePage, GamePlayPage, PlayersPage
│       └── ui/         Avatar, Icon, Toast, helpers, image
├── Dockerfile          multi-stage build cho server
├── railway.toml        config Railway (Dockerfile builder, healthcheck `/`)
└── README.md
```

## Server (CutPig/)

- **Framework**: ASP.NET Core 6, EF Core 6, Npgsql.
- **DI**:
  - `AppDbContext` (Scoped, Npgsql)
  - `TienLenScoringService` (Scoped) — pure, không phụ thuộc DB.
- **Startup** ([Program.cs](CutPig/Program.cs)):
  - Bind `0.0.0.0:$PORT` (default 8080) — bắt buộc cho Railway.
  - `ResolveConnectionString` đọc `DATABASE_URL` (kiểu Heroku/Railway URL) hoặc `ConnectionStrings:DefaultConnection`. Parse URL → Npgsql connstring với `SSL Mode=Require;Trust Server Certificate=true` (override qua `PGSSLMODE`).
  - Nếu thiếu connstring → vẫn start (dùng placeholder), DB calls sẽ fail. `/health` báo degraded.
  - CORS policy `AllowFrontend`: origin từ env `FRONTEND_ORIGIN` (CSV) + `Cors:AllowedOrigins` config + `localhost:5173/3000` mặc định.
  - DB init dùng `EnsureCreated()` + một loạt `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` để migrate idempotent (không dùng EF Migrations). Mọi cột mới phải thêm cả vào model `RoundResult`/`Player` **và** một dòng ALTER tương ứng trong Program.cs.
  - Endpoints health: `GET /` trả "running"; `GET /health` check DB connect.
- **Routes**:
  - `api/auth` — `POST /login`, `POST /logout`, `GET /me`. Chỉ `/login` public; còn lại bị `AuthMiddleware` chặn nếu thiếu Bearer token hợp lệ. Đổi username/password làm trực tiếp trong DB (không có endpoint update — chủ ý không expose CRUD account ra UI).
  - `api/players` — CRUD; `GET/PUT/DELETE /{id}/avatar`.
  - `api/games` — list, get, create, `POST /{id}/finish`, `POST /{id}/rounds`, `DELETE /{id}/rounds/{roundId}`, `DELETE /{id}` (chỉ cho game đã finished).
- **Auth** ([CutPig/Middleware/AuthMiddleware.cs](CutPig/Middleware/AuthMiddleware.cs)): mọi `/api/*` (trừ `/api/auth/login`) yêu cầu header `Authorization: Bearer <token>`. Token sinh khi login (random 32 byte base64url), lưu trong bảng `AuthTokens` cùng `ExpiresAt = now + 8 hours`. Hash mật khẩu PBKDF2-SHA256 100k iter trong [Services/PasswordHasher.cs](CutPig/Services/PasswordHasher.cs). Bootstrap user đầu tiên từ `INITIAL_USERNAME`/`INITIAL_PASSWORD` env (default `admin`/`admin`) — chỉ chạy khi bảng trống.

## Domain model

```
Player (Id, Name, Nickname?, AvatarData? string base64-data-url, CreatedAt)
Game (Id, Type=TienLenMienNam, StartedAt, FinishedAt?, Players: GamePlayer[], Rounds: GameRound[])
GamePlayer (Id, GameId, PlayerId, Seat 1..4)  -- unique (GameId, Seat)
GameRound (Id, GameId, RoundNumber, ManualScoring bool, Results: RoundResult[]) -- unique (GameId, RoundNumber)
RoundResult (Id, GameRoundId, PlayerId, Rank?, ...inputs..., Score)
GameType enum: TienLenMienNam=1, Bida9Ball=2, BidaDen=3 (chỉ TLMN được implement)
```

`RoundResult` lưu cả input (để render lại UI) **và** Score đã tính. Tổng điểm 1 player của ván = sum(Score) qua tất cả round; backend tính trong `GamesController.BuildDto`.

Cascade: xoá Game → xoá GamePlayers + GameRounds; xoá Round → xoá Results. Player FK Restrict (không xoá Player nếu đã ở trong game).

## Avatar

- Lưu trực tiếp **base64 data URL** trong cột `Players.AvatarData` (string). Limit 200 KB sau base64-decode.
- Whitelist content-type: `image/jpeg`, `image/png`, `image/webp`.
- Frontend ([client/src/ui/image.ts](client/src/ui/image.ts)) resize về 256×256 JPEG trước khi upload.
- `GET /api/players/{id}/avatar` decode data URL → trả binary với `Cache-Control: public, max-age=31536000, immutable`. Cache-bust ở client bằng query `?v=<timestamp>` trong [Avatar.tsx](client/src/ui/Avatar.tsx).

## Tien Len scoring ([CutPig/Services/TienLenScoringService.cs](CutPig/Services/TienLenScoringService.cs))

Một round = 1 trong 3 chế độ + manual fallback. Entrypoint: `Compute(inputs, manualScoring)` → gọi `ComputeCore` rồi luôn chạy `ValidateZeroSum`.

**Hằng số điểm**:
- Rank: #1=+2, #2=+1, #3=−1, #4=−2.
- Heo: đen 1, đỏ 2.
- Bonus: 3 đôi thông 3, tứ quý 4, 4 đôi thông 5.
- Về nhất 3 bích (chỉ #1, normal mode): #1 +3, mỗi player còn lại −1.
- Về chót 3 bích (chỉ #4, normal mode): #4 −3, mỗi player còn lại +1.
- Về trắng: tự +6, mỗi người khác −2.
- Phán xét self points theo số victim: 3 victim=+12, 2 victim=+9, 1 victim=+4. Loss mỗi victim −4 + held. Pardon penalty case 2 = −1.

**Chế độ**:
1. **Manual** (`manualScoring=true`): mỗi player nhập `manualScore` thẳng. Không validate khác ngoài winner/loser.
2. **White win** (`whiteWin=true` cho 1 player): winner +6, 3 người còn lại −2. Bỏ qua mọi input khác.
3. **Judge** (`judge=true` cho 1 player, kèm 1–3 `judgedVictim=true`):
   - Judge cộng điểm self theo bảng + sum của held từ các victim (heo + bonus on hand).
   - Mỗi victim −(4 + held).
   - **Case 1** (3 victim): không xử lý gì thêm.
   - **Case 2** (2 victim, 1 pardoned): pardoned −1.
   - **Case 3** (1 victim, 2 pardoned): 2 pardoned phải có rank đúng {2,3} và chơi sub-round bình thường giữa nhau (rank points + heo + bonus 1-vs-1, victim phải nằm trong scope pardoned).
4. **Normal**: 4 players, rank phải là permutation của {1,2,3,4}. Cộng rank points + heo (cut +N, lost −N) + bonus 1-vs-1 (winner +N, victim đã chọn −N).

**Validation chung** (mọi mode): round phải zero-sum — tổng `Score` = 0, và phải có ít nhất 1 player score > 0 + 1 player score < 0 (`ValidateZeroSum`). Các mode tự động (normal/whiteWin/judge) luôn zero-sum theo công thức; manual mode thì user phải tự cân đối.

`InvalidOperationException` từ scoring → controller trả `400 BadRequest` với message gốc.

## Bida 9 Ball scoring ([CutPig/Services/Bida9BallScoringService.cs](CutPig/Services/Bida9BallScoringService.cs))

Game type `Bida9Ball` cho **3 player**. 1 round = 1 ván hoàn chỉnh, kết quả lưu vào `RoundResult` cho từng player; tổng điểm game = sum theo round. Không dùng `Rank` mà dùng cấu hình bi + log các "ăn bi".

**Cấu hình ván** (lưu trong `Game.BallConfigJson`, cố định khi tạo game):
- Số bi linh hoạt: **1..9 bi** tuỳ chọn từ tập `{1..9}` (mặc định 3,6,9).
- Mỗi bi có điểm user tự gán (mặc định 3=1, 6=2, 9=3, các bi khác = số bi).

**Chế độ tính điểm round**:
1. **Phá-chấm** (`breakAndCleared=true` cho 1 player): điểm tính theo tổng cấu hình bi.
   - `S = sum(points các bi đã chọn)`.
   - Người phá `+ 2S`; mỗi (N-1) người còn lại `− 2S/(N-1)`. Với N=3: winner +2S, mỗi loser −S.
   - Server reject nếu `2S` không chia hết cho `N-1`. Frontend cảnh báo và disable submit.
   - Bỏ qua mọi input khác (không có ball hit).
2. **Bình thường**: mỗi player có list `BallHit { ball, points, victimPlayerId }` — mỗi entry = 1 lần ăn 1 bi tính điểm, kèm victim bị trừ.
   - Người ăn `+points(ball)`; victim `−points(ball)`.
   - Multi-victim hoặc single-victim cho cả tổ hợp đều support: mỗi entry chọn victim riêng.
   - `points` trong hit phải khớp cấu hình của bi.

**Validation**:
- `BallHit.ball` phải thuộc cấu hình của game; `BallHit.points` khớp cấu hình.
- `victimPlayerId` ≠ người ăn, phải thuộc bàn chơi.
- Mode phá-chấm: đúng 1 player có `breakAndCleared=true`, không player nào có ball hit.
- Zero-sum: tổng `Score` = 0 và có cả score > 0 lẫn < 0 (giống TLMN).

**Bida đền (`BidaDen`) và Bida bài**: chỉ chừa enum, sẽ implement sau — không thêm service ở phase này.

## Frontend (client/)

- **Stack**: React 18, react-router-dom v6, Vite, TypeScript.
- **API base**: `import.meta.env.VITE_API_BASE` + `/api` (set ở Vercel cho prod, default empty cho dev qua proxy).
- **Routes** ([App.tsx](client/src/App.tsx)): ngoài `LoginPage` được render khi chưa auth, các route sau chỉ accessible sau khi đăng nhập.
  - `/` → GamesPage (list)
  - `/players` → PlayersPage (CRUD + avatar)
  - `/new` → NewGamePage (chọn loại ván + người chơi)
  - `/games/:id` → GamePlayPage (gameplay + history)
- **Auth client** ([client/src/auth/AuthContext.tsx](client/src/auth/AuthContext.tsx)): token lưu `localStorage[cutpig.auth.token]`. Mỗi request tự gắn `Authorization: Bearer`. Response 401 → clear token + state về `unauthenticated` → render `LoginPage`. Bootstrap: nếu có token cũ, gọi `/api/auth/me` để verify.
- **Pages**:
  - **GamePlayPage** ([client/src/pages/GamePlayPage.tsx](client/src/pages/GamePlayPage.tsx)) là phức tạp nhất:
    - State: `inputs: PlayerInputState[]` (1 entry/player), `mode: 'normal' | 'whiteWin' | 'judge'`, `manualScoring`, `specialPlayerId`.
    - `setRank` chỉ set/clear cho 1 player; UI disable nút hạng đã bị player khác chọn (cả NormalPlayerCard và Case 3 pardoned cards).
    - `setSpecial` reset `inputs` khi đổi mode; Judge mode mặc định toàn bộ non-judge là victim (case 1).
    - `buildSubmitInputs` strip field không liên quan trước khi gửi (whiteWin → chỉ flag, judge → giữ rank/pigs/bonus cho pardoned, held cho victim).
    - Manual validate ở client: phải có cả input dương và âm (mirror server).
- **UI**: dark theme tùy biến (CSS vars trong [client/src/index.css]), `.player-grid`, `.player-card`, `.rank-badge.r1..r4`, `.score-pill.pos/neg`, `.pill-group`, `.stepper`, `.leader-row`, `.status.live/done`. Toast ở [client/src/ui/Toast.tsx](client/src/ui/Toast.tsx).

## Deploy

- **Server (Railway)**: Dockerfile multi-stage (sdk:6.0 build → aspnet:6.0 runtime). Railway tiêm `PORT`, `DATABASE_URL`. Đặt `FRONTEND_ORIGIN=https://<vercel-domain>` để CORS pass. Healthcheck `/` (vì `/health` 503 khi DB chưa lên). Optionally set `INITIAL_USERNAME`/`INITIAL_PASSWORD` cho lần bootstrap đầu — sau đó nên đổi password qua UI.
- **Frontend (Vercel)**: `client/` là root project. `VITE_API_BASE=https://<railway-domain>` (no trailing slash). Build = `npm run build`, output `dist/`.

## Quy ước

- Game type: Tien Len Mien Nam (đã có), Bida 9 Ball (đang/sẽ thêm — spec ở section trên). Bida đền và Bida bài chừa enum, implement sau.
- Game Bida 9 Ball có **3 player** (khác TLMN 4 player) — `GamePlayer.Seat` 1..3; FE `NewGamePage` cần branch theo `GameType`.
- Cấu hình bi (3 bi + điểm) là thuộc tính của Game (Bida9Ball), không phải của round → cần thêm field vào `Game` (vd `BallConfigJson`) kèm ALTER TABLE tương ứng.
- Khi thêm field vào `RoundResult`, `Player`, hoặc `Game`: nhớ thêm dòng `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` ở Program.cs (DB cũ trên Railway sẽ không tự migrate qua `EnsureCreated`).
- DTO dạng C# `record`, frontend type mirror trong `client/src/api.ts` — phải sync tay.
- Tiếng Việt cho mọi text user-facing (toast, label, error message từ scoring).
