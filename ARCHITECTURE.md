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
  - `api/players` — CRUD; `GET/PUT/DELETE /{id}/avatar`.
  - `api/games` — list, get, create, `POST /{id}/finish`, `POST /{id}/rounds`, `DELETE /{id}/rounds/{roundId}`.

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

Một round = 1 trong 3 chế độ + manual fallback. Entrypoint: `Compute(inputs, manualScoring)` → gọi `ComputeCore` rồi luôn chạy `ValidateHasWinnerAndLoser`.

**Hằng số điểm**:
- Rank: #1=+2, #2=+1, #3=−1, #4=−2.
- Heo: đen 1, đỏ 2.
- Bonus: 3 đôi thông 3, tứ quý 4, 4 đôi thông 5.
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

**Validation chung** (mọi mode): round phải có ít nhất 1 player score > 0 và 1 player score < 0 (`ValidateHasWinnerAndLoser`). Trước đây từng dùng `ValidateZeroSum` nhưng yêu cầu thực tế của user không phải zero-sum.

`InvalidOperationException` từ scoring → controller trả `400 BadRequest` với message gốc.

## Frontend (client/)

- **Stack**: React 18, react-router-dom v6, Vite, TypeScript.
- **API base**: `import.meta.env.VITE_API_BASE` + `/api` (set ở Vercel cho prod, default empty cho dev qua proxy).
- **Routes** ([App.tsx](client/src/App.tsx)):
  - `/` → GamesPage (list)
  - `/players` → PlayersPage (CRUD + avatar)
  - `/new` → NewGamePage (chọn 4 player)
  - `/games/:id` → GamePlayPage (gameplay + history)
- **Pages**:
  - **GamePlayPage** ([client/src/pages/GamePlayPage.tsx](client/src/pages/GamePlayPage.tsx)) là phức tạp nhất:
    - State: `inputs: PlayerInputState[]` (1 entry/player), `mode: 'normal' | 'whiteWin' | 'judge'`, `manualScoring`, `specialPlayerId`.
    - `setRank` chỉ set/clear cho 1 player; UI disable nút hạng đã bị player khác chọn (cả NormalPlayerCard và Case 3 pardoned cards).
    - `setSpecial` reset `inputs` khi đổi mode; Judge mode mặc định toàn bộ non-judge là victim (case 1).
    - `buildSubmitInputs` strip field không liên quan trước khi gửi (whiteWin → chỉ flag, judge → giữ rank/pigs/bonus cho pardoned, held cho victim).
    - Manual validate ở client: phải có cả input dương và âm (mirror server).
- **UI**: dark theme tùy biến (CSS vars trong [client/src/index.css]), `.player-grid`, `.player-card`, `.rank-badge.r1..r4`, `.score-pill.pos/neg`, `.pill-group`, `.stepper`, `.leader-row`, `.status.live/done`. Toast ở [client/src/ui/Toast.tsx](client/src/ui/Toast.tsx).

## Deploy

- **Server (Railway)**: Dockerfile multi-stage (sdk:6.0 build → aspnet:6.0 runtime). Railway tiêm `PORT`, `DATABASE_URL`. Đặt `FRONTEND_ORIGIN=https://<vercel-domain>` để CORS pass. Healthcheck `/` (vì `/health` 503 khi DB chưa lên).
- **Frontend (Vercel)**: `client/` là root project. `VITE_API_BASE=https://<railway-domain>` (no trailing slash). Build = `npm run build`, output `dist/`.

## Quy ước

- Game type hiện chỉ Tien Len Mien Nam; enum đã chừa chỗ cho Bida nhưng chưa có service tương ứng.
- Khi thêm field vào `RoundResult` hoặc `Player`: nhớ thêm dòng `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` ở Program.cs (DB cũ trên Railway sẽ không tự migrate qua `EnsureCreated`).
- DTO dạng C# `record`, frontend type mirror trong `client/src/api.ts` — phải sync tay.
- Tiếng Việt cho mọi text user-facing (toast, label, error message từ scoring).
