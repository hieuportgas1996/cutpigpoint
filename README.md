# CutPigPoint

Web app tính điểm Tiến Lên Miền Nam (và sẵn sàng mở rộng cho bida 9 bi, bida đền…).

## Stack

- Backend: .NET 6 (ASP.NET Core), EF Core, PostgreSQL
- Frontend: React 18 + TypeScript + Vite

## Cấu trúc

```
CutPig/                # Backend .NET 6 API
  Controllers/
  Data/                # AppDbContext (EF Core)
  Domain/              # Entities
  Dtos/
  Services/            # TienLenScoringService
client/                # Frontend React + TS
```

## Yêu cầu

- .NET SDK 6+ (project tested with SDK 8 building net6.0 target)
- Node.js 18+
- PostgreSQL 13+

## Cấu hình DB

Sửa connection string trong [CutPig/appsettings.json](CutPig/appsettings.json):

```json
"DefaultConnection": "Host=localhost;Port=5432;Database=cutpigpoint;Username=postgres;Password=postgres"
```

DB sẽ được tạo tự động khi chạy lần đầu (`EnsureCreated`).

## Chạy backend

```powershell
dotnet run --project CutPig/CutPig.csproj
```

API mặc định: http://localhost:5000 (Swagger UI ở `/swagger`).

## Chạy frontend

```powershell
cd client
npm install
npm run dev
```

Frontend ở http://localhost:5173, proxy API về `http://localhost:5000`.

## Luật tính điểm Tiến Lên Miền Nam

- Hạng: #1 +2, #2 +1, #3 −1, #4 −2 (zero-sum 4 người)
- Heo: heo đen 1đ, heo đỏ 2đ — người chặt được cộng, chủ heo bị trừ
- Bonus (cộng dồn nhiều bonus nếu có):
  - 3 đôi thông: +3
  - Tứ quý: +4
  - 4 đôi thông: +5
  - Về trắng: +6
  - Khi 1 người đạt bonus: người đó +3×bonus, mỗi người còn lại −bonus (zero-sum)
- Có option **Nhập điểm thủ công** cho từng round.

## Deploy

### Backend → Railway

1. Đăng nhập [Railway](https://railway.app) → **New Project** → **Deploy from GitHub repo** → chọn `cutpigpoint`.
2. Khi service tạo xong, vào tab **Settings** → mục **Build** chọn **Dockerfile** (đường dẫn `Dockerfile` ở root). Railway sẽ tự build từ [Dockerfile](Dockerfile).
3. Trong project, bấm **+ New** → **Database** → **PostgreSQL**. Railway sẽ inject biến `DATABASE_URL` vào service backend.
4. Mở tab **Variables** của service backend và thêm:
   - `ASPNETCORE_ENVIRONMENT` = `Production`
   - `FRONTEND_ORIGIN` = (sẽ điền sau khi deploy Vercel, ví dụ `https://cutpigpoint.vercel.app`)
   - `PGSSLMODE` = `Require` (mặc định đã ổn, set rõ cho chắc)
5. Tab **Settings** → **Networking** → bấm **Generate Domain** để có public URL (dạng `https://xxx.up.railway.app`).
6. Test: mở `https://xxx.up.railway.app/health` — phải trả về `{"status":"ok"}`.

`Program.cs` tự parse `DATABASE_URL` (URI scheme `postgresql://…`) sang Npgsql connection string và lắng nghe trên `$PORT` Railway cấp.

### Frontend → Vercel

1. Đăng nhập [Vercel](https://vercel.com) → **Add New** → **Project** → import repo `cutpigpoint`.
2. Trong màn hình config:
   - **Root Directory**: `client`
   - **Framework Preset**: Vite (Vercel tự nhận)
   - **Build Command** / **Output Directory**: để mặc định (đã khoá trong [client/vercel.json](client/vercel.json))
3. Mục **Environment Variables**:
   - `VITE_API_BASE` = URL Railway từ bước trên (ví dụ `https://xxx.up.railway.app`, KHÔNG có dấu `/` cuối)
4. Bấm **Deploy**.
5. Sau khi deploy xong, copy domain Vercel (ví dụ `https://cutpigpoint.vercel.app`) → quay lại Railway → cập nhật biến `FRONTEND_ORIGIN` → Railway tự redeploy.

### Verify

- Mở Vercel URL → vào trang **Người chơi**, thử thêm 1 player. Nếu lưu được = backend + DB + CORS đều OK.
- Nếu lỗi CORS: check biến `FRONTEND_ORIGIN` trên Railway phải khớp **chính xác** với origin Vercel (không có `/` cuối, đúng scheme `https://`).

## Roadmap

- [ ] Bida 9 bi
- [ ] Bida đền
- [ ] Auth/đăng nhập
- [ ] Thống kê người chơi (ELO, win rate)
