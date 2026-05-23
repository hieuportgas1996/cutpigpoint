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

Các bộ đặc biệt **vượt loại** để chặt **con 2** hoặc đôi/sám 2:

| Bộ chặt | Chặt được |
|---|---|
| **3 đôi thông** | 1 con 2 |
| **Tứ quý** | 1 con 2, đôi 2, 3 đôi thông |
| **4 đôi thông** | Tối đa **2 con 2** (1 con 2, hoặc đôi 2). **Không** chặt được sám 2 và tứ quý 2 (sám 2 là bộ mạnh nhất, không thể chặt). |

**Lưu ý**: **Sám 2** là bộ mạnh nhất trong game, **không có bộ nào chặt được**.

### Quy tắc "phải có lượt" (turn order with pass tracking)

- Trong cùng 1 trick, người đã **bỏ lượt** (pass) thì **bị skip ở các lượt sau của trick đó**.
- Ví dụ 4 người:
  - P1 đánh `3` → P2 bỏ → P3 đánh `4` → P4 đánh `5` → P1 đánh `6` → **P3 đánh tiếp** (P2 đã bỏ, skip).
- Khi tất cả người khác đều pass → người đánh lá mạnh nhất thắng trick → **trick reset** → mọi người tham gia lại trick mới (bao gồm cả người đã pass).

### Ngoại lệ "4 đôi thông"
- 4 đôi thông có thể đánh ra **bất kỳ lúc nào** trong lượt của mình, kể cả nếu đã pass trick này hoặc đối thủ đang đánh con 2 / đôi 2.
- Ví dụ: P1 đánh `2♥`, P2 đã pass trick này từ trước, nhưng **P2 vẫn được phép chặt** bằng 4 đôi thông (chặt 1 con 2).
- **Chặn quyền mở trick mới**: khi một người vừa thắng trick (mọi người khác pass) và sắp được mở trick mới, người có 4 đôi thông trong tay **có nút "Chặn"** để ngắt, buộc trick hiện tại tiếp tục bằng 4 đôi thông của mình (chặt con 2 / đôi 2 vừa thắng trick).
  - Nếu **không** bấm Chặn → người thắng trick mở trick mới như bình thường.
  - Nếu bấm Chặn → đánh 4 đôi thông ra, đối thủ không có cách chặn lại (sám 2 không tồn tại trong tay đối thủ vì 4 con 2 đã ở đâu đó, và không có bộ nào khác chặn được 4 đôi thông).
  - Cho phép trường hợp người chơi cố tình "giả vờ pass" để giấu 4 đôi thông, chờ thời điểm vàng.

## Về trắng (white-win)

Sau khi chia bài, người chơi **được quyền chọn về trắng** (thắng ngay không cần đánh) nếu có **một trong** các bộ sau:

1. **Sảnh từ 3 đến A (12 lá)**: bất kỳ chất nào, mỗi rank 3,4,...,A xuất hiện đúng 1 lần.
2. **Tứ quý 2**: cả 4 con `2♠ 2♣ 2♦ 2♥`.
3. **6 đôi**: bất kỳ 6 đôi nào trong 13 lá.
4. **5 đôi thông**: 5 đôi rank liên tiếp (không chứa 2).

### Quyền chọn về trắng
- Sau khi chia bài, nếu phát hiện bộ về trắng, người chơi được hỏi **"Về trắng?"** với 2 lựa chọn:
  - **Có** → tự động thắng, ván kết thúc, tính điểm về trắng.
  - **Không** → đánh tiếp như ván bình thường (mất quyền về trắng cho ván này).
- Có timeout 20s để chọn; hết giờ không chọn = **không** về trắng, đánh bình thường.

### Tính điểm về trắng

Mỗi người **về trắng** ăn của mỗi người **không về trắng** **+2 điểm** (zero-sum).

- **1 người về trắng**: người trắng +2 × (số người còn lại); mỗi người không trắng -2.
  - Ví dụ 4 người, 1 trắng: trắng **+6**, mỗi người kia **-2**.
  - Ví dụ 3 người, 1 trắng: trắng **+4**, mỗi người kia **-2**.
  - Ví dụ 2 người, 1 trắng: trắng **+2**, người kia **-2**.
- **Nhiều người cùng về trắng**: mỗi người không trắng đóng -2 cho mỗi người trắng; tổng điểm âm đó chia đều cho các người trắng.
  - Ví dụ 4 người, 2 trắng + 2 không trắng: mỗi người không trắng **-2** (tổng -4 từ 2 người); 2 người trắng chia nhau **+4** → mỗi người trắng **+2**.
  - Ví dụ 4 người, 3 trắng + 1 không trắng: người không trắng **-6** (-2 × 3 người trắng); 3 người trắng chia nhau **+6** → mỗi người trắng **+2**.
- Ván kết thúc ngay sau khi (các) người chọn về trắng xác nhận, không đánh.

**Lưu ý**: Nếu tất cả người có bộ về trắng đều **chọn không** về trắng → ván đánh bình thường, tính điểm theo bảng rank.

## Tính điểm ván thường (không về trắng)

### Điểm theo rank

| Số người | Nhất | Nhì | Ba | Tư |
|---|---|---|---|---|
| 4 | +2 | +1 | -1 | -2 |
| 3 | +2 | 0 | -2 | — |
| 2 | +1 | -1 | — | — |

### Thưởng "chặt heo" (cộng vào điểm ván)

Mỗi combo "có chop value" đánh ra trong 1 trick được tích lại. Khi trick kết thúc (tất cả pass và trick reset, hoặc round end giữa trick), **người chặt cuối cùng** trong chain ăn toàn bộ pot từ **người bị chặt cuối cùng** (= người đánh combo bị chặt cuối). Các người ở giữa chain net 0.

**Bảng chop value**:

| Combo | Điểm |
|---|---|
| Heo đen (lá `2♠` / `2♣`) | 1 mỗi con |
| Heo đỏ (lá `2♦` / `2♥`) | 2 mỗi con |
| Đôi 2 | Cộng điểm 2 con (VD `2♠+2♥` = 1+2 = 3đ) |
| 3 đôi thông | 3 |
| Tứ quý non-2 | 4 |
| 4 đôi thông | 5 |

**Ví dụ chain**: P1 đánh `2♠` (1đ heo treo) → P2 đánh `2♥` (2đ treo) → P3 đánh 3-đôi-thông (3đ treo) → P4 đánh tứ quý.
- Last cutter = P4. Người bị chặt cuối = P3.
- Pot = 1+2+3 = 6 (sum chop value tất cả combo trước P4).
- P3 **-6** (cho P4); P4 **+6**; P1, P2 net 0.

**Lưu ý**:
- **Same-kind đơn (single 2 chặn single 2) KHÔNG tính**: chain reset = 0. Vd P1=`2♠`, P2=`2♥` → P1, P2 đều net 0. Chỉ khi last cutter dùng đôi 2 / sám 2 / tứ quý / 3-đôi-thông / 4-đôi-thông mới settle pot.
- Đôi 2 chặn đôi 2 (same-kind đôi) **vẫn tính**.
- Sám 2 và tứ quý 2 unbeatable theo luật chặt heo → không bao giờ kích hoạt chop chain, không ai bị trừ.
- Tứ quý không chặt được sám 2 / tứ quý 2 (nhưng chặt 3-đôi-thông, đôi 2, lá 2).

### Phán xử (Judge)

Khi **người về Nhất** vừa hết bài, server check các player còn lại: ai **chưa đánh ra lá nào** trong ván này (HasPlayedThisRound = false) → là **nạn nhân** (victim). Nếu có ≥ 1 victim → phán xử kích hoạt và **thay thế toàn bộ scoring** của ván (bỏ qua base rank, chop-pig, 3♠).

**Điểm bị xử** = `4 + held` mỗi victim, với `held` = giá trị các bộ còn cầm trong tay:
- Heo đen (`2♠` / `2♣`): 1 mỗi con.
- Heo đỏ (`2♦` / `2♥`): 2 mỗi con.
- Tứ quý (4 lá cùng rank, bất kỳ): +4.
- 3 đôi thông: +3.
- 4 đôi thông: +5 (nếu vừa có 4 đôi thông và tứ quý → cộng cả 2).

**Người Nhất** ăn tổng điểm phạt của các victim.

**Phân loại theo số victim / pardoned** (pardoned = player đã ra ít nhất 1 lá, không phải Nhất):

| Case | Victim | Pardoned | Hành động | Điểm |
|---|---|---|---|---|
| A | tất cả còn lại | 0 | Kết thúc ván ngay | Mỗi victim -(4+held), Nhất +∑ |
| B | còn 1 người đã ra bài | 1 | Kết thúc ván ngay | Mỗi victim -(4+held), pardoned **-1**, Nhất +∑+1 |
| C | còn ≥2 người đã ra bài | ≥2 | Victim được set rank chót; pardoned **tiếp tục chơi** xác định Nhì/Ba | Victim -(4+held), Nhất +(4+held). Sau khi pardoned đánh xong: áp base rank +1/-1 (2 người) hoặc +2/0/-2 (3 người); chop-pig + 3♠ + **đui 3♠** vẫn tính giữa các pardoned. Đui 3♠ trong Case C: pardoned hạng chót (sub-round) còn 3♠ → -3 / mỗi pardoned khác +1 (zero-sum trong nhóm pardoned). |

**Áp dụng với mọi n ≥ 2**. Không áp khi về trắng.

**Stack với 3♠**: Nếu người Nhất về bằng `3♠` cuối + có phán xử → **cộng thêm** thưởng 3♠ (`+(n-1)` cho Nhất, `-1` cho mỗi người khác) lên trên điểm phán xử. Đui 3♠ (Chót còn `3♠` trong tay) trong phán xử **không** tính.

**Ví dụ** (4 người, Case A — cả P1/P2/P3 chưa ra bài, P4 về Nhất):
- P1: không cầm gì → -4.
- P2: cầm 1 `2♥` → held=2, -(4+2) = **-6**.
- P3: cầm 3 đôi thông → held=3, -(4+3) = **-7**.
- P4: +4+6+7 = **+17**.

**Ví dụ Case C** (P1 chưa ra, P2/P3 đã ra, P4 Nhất):
- P1 victim, không cầm gì: **-4**.
- P4 nhận **+4**.
- P2 & P3 chơi tiếp; xong áp +1/-1 (P2 Nhì = +1, P3 Ba = -1) + chop-pig nếu có.

### Thưởng / phạt `3♠` cuối

- **Thắng cuối bằng `3♠`** (về Nhất, lá cuối đánh ra chứa `3♠`): người Nhất **+(n-1)**, mỗi người khác **-1**. Zero-sum.
- **Đui `3♠`** (về Chót, kết ván vẫn còn `3♠` trong tay): người Chót **-3**, mỗi người khác **+1**. **Không** zero-sum khi <4 người (3 người: -3/+1/+1 = -1; 2 người: -3/+1 = -2).
- Không áp dụng khi về trắng.
- Cả 2 cộng/trừ vào điểm ván (sau base rank và sau chop-pig).

## Trận đấu nhiều ván

- Một **phòng** = một **trận**, gồm nhiều **ván** liên tiếp.
- Sau mỗi ván:
  - Bảng kết quả ván hiển thị (rank, điểm ván, **tổng điểm cộng dồn** trong trận).
  - **Đếm ngược 20 giây** rồi tự động sang ván tiếp.
  - Host có nút **"Bắt đầu ngay"** để skip countdown deal ván tiếp luôn, và nút **"Kết thúc trận"** nếu muốn dừng hẳn (đóng phòng, broadcast `MatchEnd`).
- Ván tiếp:
  - Chia bài lại (13 lá/người, bài dư úp).
  - Detect về trắng lại; nếu lại có về trắng → ván tiếp đó kết thúc ngay, đếm tiếp 5s.
  - Người **Nhất ván trước** đi đầu (mở trick đầu).
- Ván đầu tiên (round 1):
  - Nếu **3♠ được chia trong tay người chơi**: người giữ `3♠` đi đầu và **nước đầu phải chứa `3♠`**.
  - Nếu **3♠ nằm trong bài úp** (2-3 người chơi, 13 lá dư bị bỏ): seat 0 (host) đi đầu, **không bắt buộc** nước đầu chứa lá nào.

## Quy tắc khác

- **Disconnect**: timer **60 giây/lượt**. Hết giờ không action → auto-pass. Auto-pass khi đang mở nước (không có trick để chặn) → tự động đánh lá nhỏ nhất trong tay.
- **Reconnect**: tay bài (`PrivateHand`) được gửi lại tự động sau khi connect lại; state ván vẫn còn (in-memory hiện tại — Railway restart giữa trận = mất ván).
- **Host xoá phòng**: chỉ khi phòng đang chờ (`Waiting`). Phòng đang chơi không xoá được.
- **Admin**: có thể xoá **bất kỳ phòng nào**, kể cả đang chơi hoặc đã kết thúc; cũng thấy được tất cả phòng (không chỉ phòng đang chờ).
