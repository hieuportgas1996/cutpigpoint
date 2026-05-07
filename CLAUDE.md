# CutPigPoint

App chấm điểm Tiến Lên Miền Nam (4 người). Server: ASP.NET Core 6 + EF Core + PostgreSQL. Client: React + Vite + TypeScript. Deploy: Railway (API + DB) + Vercel (frontend).

Đọc [ARCHITECTURE.md](ARCHITECTURE.md) để có map đầy đủ về layout, domain model, scoring rules, và deploy. Bắt đầu từ đó trước khi grep.

## Quy ước nhanh
- Tiếng Việt cho mọi text user-facing.
- DB schema migrate bằng `EnsureCreated()` + chuỗi `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` trong [CutPig/Program.cs](CutPig/Program.cs). Thêm field mới vào `RoundResult`/`Player` ⇒ phải thêm cả ALTER tương ứng.
- DTO server (C# record) và type client (`client/src/api.ts`) sync tay.
- Scoring logic ở [CutPig/Services/TienLenScoringService.cs](CutPig/Services/TienLenScoringService.cs); validate cuối: round phải có cả score > 0 và score < 0 (không bắt buộc zero-sum).
