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

## Roadmap

- [ ] Bida 9 bi
- [ ] Bida đền
- [ ] Auth/đăng nhập
- [ ] Thống kê người chơi (ELO, win rate)
