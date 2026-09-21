# shotlink

タスクバーのアイコンを押す → 範囲をドラッグ → 離した瞬間に URL がクリップボードに入っている。
画像は自分の Cloudflare R2 に置き、推測できないランダム URL で共有する。

```
常駐 → アイコンをクリック → 画面が暗転して十字カーソル → ドラッグ
     → 離した瞬間にキャプチャ → アップロード → URL をコピー → 右下に通知
```

## 構成

| | |
|---|---|
| `worker/index.js` | アップロードの受け口と画像配信 (Cloudflare Workers + R2) |
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
```

トークンはここにしか置かない。リポジトリには入れない。

## 使い方

- ピン留めしたアイコンをクリック → 範囲をドラッグ
- `Esc` / 右クリック / ごく小さいドラッグ → キャンセル
- 通知をクリック → その画像をブラウザで開く
- アップロードに失敗したときは `%USERPROFILE%\Pictures\shotlink\` に保存し、
  そのパスをクリップボードに入れる (撮ったものは失わない)

### コマンドライン

| | |
|---|---|
| `shotlink.exe` | 常駐していれば範囲選択を開始、していなければ常駐しつつ開始 |
| `shotlink.exe --background` | 常駐だけする (自動起動で使う) |
| `shotlink.exe --quit` | 常駐を終了する |
| `shotlink.exe --capture-full` | 画面全体を撮って送る。結果は `%APPDATA%\shotlink\last-run.log` |

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
