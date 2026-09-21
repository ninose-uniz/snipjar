# shotlink

タスクバーのアイコンを押す → 範囲をドラッグ → 「コピー」か「保存」を選ぶ。
撮ったものは自分の Cloudflare R2 にも貯まり、ブラウザの一覧ページで見返せる。

```
常駐 → アイコンをクリック → 画面が暗転して十字カーソル → ドラッグ
     → 離すと選択範囲の脇にバーが出る → コピー / 保存 → 右下に通知
                                        ↘ 裏で R2 にも上がり、一覧に並ぶ
```

## 構成

| | |
|---|---|
| `worker/index.js` | アップロードの受け口・画像配信・一覧ページ (Cloudflare Workers + R2) |
| `client/*.cs` | 常駐アプリ (C# / WinForms, .NET Framework 4.8) |
| `build.ps1` | Windows 同梱の `csc.exe` でビルド。.NET SDK も NuGet も要らない |
| `install.ps1` | 配置・自動起動の登録・ショートカット作成 |

## サーバー側の準備

```powershell
npx wrangler r2 bucket create shotlink
npx wrangler secret put UPLOAD_TOKEN   # 長いランダム文字列を貼る
npx wrangler deploy
```

R2 は Cloudflare ダッシュボードで有効化しておく (無料枠 10GB)。

## クライアント側の準備

```powershell
.\install.ps1
```

配置先・自動起動の登録内容を表示してから確認を求める。終わったらスタートメニューの
shotlink を右クリックして「タスクバーにピン留めする」。

設定は `%APPDATA%\shotlink\config.ini`:

```ini
endpoint=https://shotlink.<subdomain>.workers.dev/upload
token=<UPLOAD_TOKEN と同じ値>
upload=always   ; never にすると R2 には一切上げない (一覧にも残らない)
savedir=        ; 「保存」の保存先。省略時は Pictures\shotlink
```

トークンはここにしか置かない。リポジトリには入れない。

## 使い方

- ピン留めしたアイコンをクリック → 範囲をドラッグ → 離すとバーが出る
  - **コピー** (`C`) … 画像をクリップボードへ。Discord や Slack にそのまま貼れる
  - **保存** (`S`) … `Pictures\shotlink\` に PNG
  - `Esc` またはバーの外をクリック → 破棄。押すまで何も起きない
- 範囲選択中のキャンセルは `Esc` / 右クリック / ごく小さいドラッグ
- どちらを押した場合も、裏で R2 にも上がって一覧に残る (`upload=never` で止まる)
- アップロードに失敗したときは `Pictures\shotlink\` に保存し、そのパスを
  クリップボードに入れる (撮ったものは失わない)

## 一覧ページ

`https://shotlink.<subdomain>.workers.dev/gallery`

初回だけトークンを入力する。以後は HttpOnly Cookie で開ける (トークンは URL に載せない)。
サムネイル・日時・サイズが新しい順に並び、`URL をコピー` と `削除` ができる。
サムネイルをクリックすると原寸が開く。

### コマンドライン

| | |
|---|---|
| `shotlink.exe` | 常駐していれば範囲選択を開始、していなければ常駐しつつ開始 |
| `shotlink.exe --background` | 常駐だけする (自動起動で使う) |
| `shotlink.exe --quit` | 常駐を終了する |
| `shotlink.exe --capture-full` | 画面全体を撮って送る。結果は `%APPDATA%\shotlink\last-run.log` |

## 作りの理由（追記）

- **一覧のために別の索引を持たない**。R2 の `list()` は辞書順しか返さないので、キーの先頭に
  反転タイムスタンプを埋め、辞書順＝新しい順にした。ランダム部 16 文字が推測不能性を担う。
- **サムネイルはクライアントが作る**。Workers 単体では画像を縮小できず、原寸 PNG を並べると
  1 ページ数十 MB になるため。JPEG のほうが大きくなる小さい画像では送らず、原寸にフォールバックする。
- **一覧のトークンは Cookie に入れる**。URL に載せると履歴や共有リンクに残る。
- **URL コピーは `execCommand` にフォールバックする**。`navigator.clipboard` は
  フォーカスと許可を要求し、埋め込みビューでは拒否されることがある。

## 作りの理由

- **ピン留めした exe のクリックで起動する**。クリックで起動した 2 個目のプロセスは
  常駐中の本体に合図を送って即終了するので、タスクバーにボタンは出ず待ち時間もない。
  合図の前に `AllowSetForegroundWindow` を呼び、前面に出る権利を本体へ渡している。
- **オーバーレイはモーダルにしない**。`ShowDialog` だと前回の通知が閉じるだけで
  選択中のオーバーレイごと巻き込まれて閉じる。`Show()` + `FormClosed` で受ける。
- **先に画面を撮ってからオーバーレイを出す**。切り出しは撮影済みのビットマップから
  行うので、暗転やちらつきが写り込まない。
- **選択の終点はマウスアップの座標を使う**。Windows は `WM_MOUSEMOVE` を
  キューが空のときしか作らないため、ボタンアップが最後の移動を追い越す。
- **通知領域にアイコンを置かない**。右下の通知は自前のウィンドウで描いている。
