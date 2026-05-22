# CutPigPoint

App chấm điểm + chơi online Tiến Lên Miền Nam (2-4 người). Có 2 chế độ:
- Chấm điểm thủ công offline (legacy).
- Chơi online realtime qua SignalR (lobby + gameplay TLMN có engine luật server-side).

Stack: ASP.NET Core 6 + EF Core + SignalR + PostgreSQL · React + Vite + TypeScript · Railway + Vercel.

Đọc [ARCHITECTURE.md](ARCHITECTURE.md) để có map đầy đủ về layout, domain model, hub events, scoring, mobile responsive. Đọc [RULE.md](RULE.md) cho luật TLMN chi tiết (chặt heo, về trắng, pass-tracking). **Bắt đầu từ 2 file đó trước khi grep.**

## Quy ước nhanh

- **Tiếng Việt** cho mọi text user-facing (toast, label, error, UI string, kể cả message thrown từ scoring/engine).
- **DB schema migrate** bằng `EnsureCreated()` + chuỗi `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` (và `CREATE TABLE IF NOT EXISTS` cho bảng mới) trong [CutPig/Program.cs](CutPig/Program.cs). Thêm field vào `RoundResult`/`Player`/`AppUser`/`Room`/`RoomSeat` ⇒ phải thêm ALTER tương ứng.
- **DTO sync tay**: C# record trong [CutPig/Dtos/Dtos.cs](CutPig/Dtos/Dtos.cs) ↔ TS interface trong [client/src/api.ts](client/src/api.ts).
- **Card combo logic ở 2 nơi**: server [CutPig/Game/TienLenCombo.cs](CutPig/Game/TienLenCombo.cs) là source of truth, client [client/src/game/cards.ts](client/src/game/cards.ts) là mirror cho UX. Đổi rule = sửa cả 2.
- **Auth**: multi-user, admin tạo account đưa username/password cho người chơi (không self-signup). Token Bearer 8h trong bảng `AuthTokens`. `AuthMiddleware` chặn `/api/*` trừ `/api/auth/login` và `/hubs/*`. Bootstrap admin từ env `INITIAL_USERNAME`/`INITIAL_PASSWORD` (default `admin`/`admin`, `IsAdmin=true`).
- **SignalR auth**: token gửi qua query string `?access_token=...` (không phải header). Hub [CutPig/Hubs/RoomHub.cs](CutPig/Hubs/RoomHub.cs) tự validate trong `AuthenticateAsync()`.
- **Match state in-memory** trong `MatchManager` (Singleton) + `MatchTimerService` (HostedService) auto-pass 45s/lượt + auto-next-round 5s + auto-resolve `WhiteWinChoice` 10s + auto-finalize `PendingTrickCut` 5s. Railway restart giữa ván = mất ván — chưa persist DB.
- **CORS phải `AllowCredentials()`** cho SignalR; CORS origin lấy từ env `FRONTEND_ORIGIN` + localhost mặc định.
- **Scoring**:
  - Online (TLMN): rank → +2/+1/-1/-2 (4 người), +2/0/-2 (3), +1/-1 (2). Cộng dồn `TotalScore` qua các ván trong cùng trận.
  - **Chop-pig bonus**: mỗi trick, chain các combo có chop value (heo 1-2đ, 3-đôi-thông 3đ, tứ quý 4đ, 4-đôi-thông 5đ); last cutter ăn sum(chain[0..^1]) từ second-to-last. Cộng vào điểm ván (xem `ChopBonus` trong `RoundResultEntryDto`).
  - **3♠ cuối**: Nhất thắng bằng lá cuối chứa `3♠` → +(n-1) / others -1; Chót còn `3♠` trong tay → -3 / others +1 (không zero-sum khi <4 người). Không áp khi white-win.
  - Về trắng zero-sum multi-winner: mỗi loser **-2 × số winner**, mỗi winner **+2 × số loser** (1 trắng / 3 thua → trắng +6, mỗi thua -2; 2 trắng / 2 thua → mỗi trắng +4, mỗi thua -4). Là **opt-in**: candidate có 10s chọn Có/Không, không ai chọn → chơi bình thường.
  - Legacy manual ([CutPig/Services/TienLenScoringService.cs](CutPig/Services/TienLenScoringService.cs)): validate round phải zero-sum (tổng = 0) và có cả score > 0 lẫn < 0.

## Cảnh báo bug đã gặp

- **EF tracking conflict**: không gắn `User = authUser` vào navigation property của entity mới (`RoomSeat`, etc.) — chỉ set `UserId`. Pattern `_db.RoomSeats.Add(new RoomSeat { UserId = ... })` an toàn hơn.
- **React Hooks order**: derive values + `useMemo` phải đặt **trước** mọi `if (...) return` trong component, dùng optional chaining cho data có thể null.
- **SVG text overflow**: dùng `<g transform="rotate(180 cx cy)">` bọc cả group thay vì rotate từng text với `textAnchor="end"` — tránh text tràn khỏi viewBox.
