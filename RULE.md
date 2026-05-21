# Luật Tiến Lên Miền Nam — CutPigPoint

Tài liệu chính thức về luật bài áp dụng cho game online trong app.

## Số người chơi
- 2, 3 hoặc 4 người. Tối đa 4.
- Mỗi người **13 lá**, bất kể số người chơi.
- Bài dư (nếu < 4 người) bị úp, không ai biết nội dung.

## Thứ tự bài

### Rank (giá trị)
Từ thấp đến cao: **3 < 4 < 5 < 6 < 7 < 8 < 9 < 10 < J < Q < K < A < 2**

### Chất (tie-break trong cùng rank)
Từ thấp đến cao: **♠ (bích) < ♣ (chuồn) < ♦ (rô) < ♥ (cơ)**

### Ví dụ
- `3♠ < 3♣ < 3♦ < 3♥ < 4♠ < ... < A♥ < 2♠ < 2♣ < 2♦ < 2♥`
- Lá lớn nhất bộ bài: **2♥**

## Combo (bộ bài đánh ra)

| Loại | Số lá | Ghi chú |
|---|---|---|
| **Lẻ** (single) | 1 | |
| **Đôi** (pair) | 2 | Cùng rank |
| **Sám** (triple) | 3 | Cùng rank |
| **Tứ quý** (four) | 4 | Cùng rank |
| **Sảnh** (run) | ≥ 3 | Liên tiếp rank, **không chứa 2** |
| **Đôi thông** (run of pairs) | ≥ 6 (≥ 3 đôi) | Đôi liên tiếp, **không chứa 2** |

### Quy tắc chặn (Beat)

**Cùng loại, cùng độ dài, rank cao hơn** chặn được nhau.
- Ví dụ: `5♥` chặn `5♦`. `8-8` chặn `7-7`. `5-6-7` chặn `4-5-6`.

### Quy tắc chặt heo (cut)

Các bộ đặc biệt **vượt loại** để chặt **con 2** hoặc đôi/sám/tứ quý 2:

| Bộ chặt | Chặt được |
|---|---|
| **3 đôi thông** | 1 con 2 |
| **Tứ quý** | 1 con 2, đôi 2, 3 đôi thông |
| **4 đôi thông** | Tất cả (con 2, đôi 2, sám 2, tứ quý 2, 3 đôi thông) |

### Quy tắc "phải có lượt" (turn order with pass tracking)

- Trong cùng 1 trick, người đã **bỏ lượt** (pass) thì **bị skip ở các lượt sau của trick đó**.
- Ví dụ 4 người:
  - P1 đánh `3` → P2 bỏ → P3 đánh `4` → P4 đánh `5` → P1 đánh `6` → **P3 đánh tiếp** (P2 đã bỏ, skip).
- Khi tất cả người khác đều pass → người đánh lá mạnh nhất thắng trick → **trick reset** → mọi người tham gia lại trick mới (bao gồm cả người đã pass).

### Ngoại lệ "4 đôi thông"
- 4 đôi thông có thể đánh ra **bất kỳ lúc nào** trong lượt của mình, kể cả nếu đã pass trick này hoặc đối thủ đang đánh con 2.
- Ví dụ: P1 đánh `2♥`, P2 đã pass trick này từ trước, nhưng **P2 vẫn được phép chặt** bằng 4 đôi thông.

## Về trắng (white-win)

Sau khi chia bài, người chơi **tự động về trắng** (thắng ngay không cần đánh) nếu có **một trong** các bộ sau:

1. **Sảnh từ 3 đến A (12 lá)**: bất kỳ chất nào, mỗi rank 3,4,...,A xuất hiện đúng 1 lần.
2. **Tứ quý 2**: cả 4 con `2♠ 2♣ 2♦ 2♥`.
3. **6 đôi**: bất kỳ 6 đôi nào trong 13 lá.
4. **5 đôi thông**: 5 đôi rank liên tiếp (không chứa 2).

### Tính điểm về trắng
- Người về trắng: **+6**.
- Mỗi người khác: **-2**.
- Ván kết thúc ngay sau khi chia, không đánh.

## Tính điểm ván thường (không về trắng)

Theo rank kết thúc trong ván:

| Số người | Nhất | Nhì | Ba | Tư |
|---|---|---|---|---|
| 4 | +2 | +1 | -1 | -2 |
| 3 | +2 | 0 | -2 | — |
| 2 | +1 | -1 | — | — |

## Trận đấu nhiều ván

- Một **phòng** = một **trận**, gồm nhiều **ván** liên tiếp.
- Sau mỗi ván:
  - Bảng điểm cộng dồn hiển thị cho mọi người.
  - Host quyết định **"Ván tiếp"** hoặc **"Kết thúc trận"**.
- Ván tiếp:
  - Chia bài lại.
  - Người **Nhất ván trước** đi đầu (mở trick đầu).
- Ván đầu tiên (round 1):
  - Người có **3 bích** (`3♠`) đi đầu, và **nước đầu phải chứa `3♠`**.

## Quy tắc khác

- **Disconnect**: timer 30 giây/lượt. Hết giờ không action → auto-pass. Auto-pass khi đang mở nước (không có trick để chặn) → tự động đánh lá nhỏ nhất.
- **Reconnect**: tay bài được gửi lại sau khi connect lại; state ván vẫn còn (in-memory hiện tại, sẽ chuyển DB ở phase sau).
- **Host xoá phòng**: chỉ khi phòng đang chờ (Waiting). Phòng đang chơi không xoá được.
- **Admin**: có thể xoá bất kỳ phòng nào, kể cả đang chơi hoặc đã kết thúc.
