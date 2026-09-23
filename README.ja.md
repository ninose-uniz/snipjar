# Snipjar

範囲をドラッグして「コピー」か「保存」を選ぶだけ。撮ったものは **自分の** Cloudflare R2 に
貯まり、どこからでも開ける一覧ページで見返せる。

English: [README.md](README.md)

```
常駐 → ピン留めしたアイコンをクリック → 画面が暗転して十字カーソル → ドラッグ
     → 離すと選択範囲の脇にバーが出る → コピー / 保存 → 右下に通知
                                        ↘ 裏で R2 にも上がり、一覧に並ぶ
```

![離すと出るバー](docs/actionbar.png)
![右下の通知](docs/toast.png)
![一覧ページ](docs/gallery.png)

## なぜ作ったか

他人のサーバーを一切通さないから。アカウント登録もなく、無料枠を使い切って困ることもなく、
スクショを預かる会社も存在しない。Worker も R2 バケットも **あなたのアカウントの中** にあり、
あなたがデプロイして、作者は中身を見られない。止められる相手もいない。

## 必要なもの

- Windows 10 / 11
- **R2 を有効化した** Cloudflare アカウント（無料枠 10GB。有効化にカード登録が必要）
- [Node.js](https://nodejs.org/) — Cloudflare へのデプロイに使う `wrangler` のため

.NET SDK は不要。クライアントは Windows 同梱の `csc.exe` でビルドする。

## 導入

1. [Releases](https://github.com/ninose-uniz/snipjar/releases/latest) から zip を落として展開
2. そのフォルダで PowerShell を開いて実行:

   ```powershell
   .\setup.ps1
   ```

   Cloudflare へのログイン、R2 バケット作成、Worker のデプロイ、トークンの発行、設定ファイルの
   作成、常駐アプリの配置まで通しでやる。アカウントに何を作るかは、作る前に表示して確認する。
3. スタートメニューの **Snipjar** を右クリック → **タスクバーにピン留めする**

### Windows が警告を出します

「Windows によって PC が保護されました」と出る。これは **コード署名をしていない** ため。
証明書は毎年費用がかかり、しかも入れてもしばらくは警告が消えない。納得できるなら
**詳細情報 → 実行** を押す。

信用できないなら、リリースの `SHA256SUMS.txt` と照合してほしい:

```powershell
Get-FileHash .\snipjar.exe -Algorithm SHA256
```

リリースを使わず、自分でビルドしてもいい。`build.ps1` は Windows 以外に何も要らない。

## 使い方

| | |
|---|---|
| ピン留めしたアイコンをクリック | 画面が暗転するので範囲をドラッグ |
| `Esc` / 右クリック / ごく小さいドラッグ | 範囲選択をやめる |
| **コピー** または `C` | 画像をクリップボードへ。Discord や Slack にそのまま貼れる |
| **保存** または `S` | `Pictures\snipjar\` に PNG |
| `Esc` / バーの外をクリック | 破棄。**押すまで何も起きない** |

どちらを押した場合も、裏で R2 にも上がって一覧に残る。
アップロードに失敗したときは `Pictures\snipjar\` に保存してパスをクリップボードに入れる
（撮ったものは失わない）。

## 一覧ページ

`https://snipjar.<自分のサブドメイン>.workers.dev/gallery`

初回だけトークンを貼る。以後は HttpOnly Cookie で開ける（トークンは URL に載せない）。
新しい順にサムネイル・日時・サイズが並び、`URL をコピー` と `削除` ができる。
サムネイルをクリックすると原寸が開く。

## 設定

`%APPDATA%\snipjar\config.ini`

```ini
endpoint=https://snipjar.<自分のサブドメイン>.workers.dev/upload
token=<Worker の UPLOAD_TOKEN と同じ値>
upload=always      ; never にすると R2 には一切上げない（全部ローカルで完結）
savedir=           ; 「保存」の保存先。省略時は Pictures\snipjar
updatecheck=       ; off にすると GitHub への更新確認をしない
```

書き換えたら常駐を入れ直す:

```powershell
& "$env:LOCALAPPDATA\snipjar\snipjar.exe" --quit
Start-Process "$env:LOCALAPPDATA\snipjar\snipjar.exe" -ArgumentList '--background'
```

**注意:** 多くの環境で `Pictures` は `OneDrive\画像` に解決される。同期させたくなければ
`savedir` にローカルのパスを指定する。

### 通信するのはどこか

- アップロード先は自分の Worker だけ。他はどこにも送らない
- 1 日 1 回だけ `api.github.com` に新しいリリースがあるか聞き、あれば通知を出す。
  勝手にダウンロードもインストールもしない。`updatecheck=off` で止まる

## コマンドライン

| | |
|---|---|
| `snipjar.exe` | 常駐していれば範囲選択を開始、していなければ常駐しつつ開始 |
| `snipjar.exe --background` | 常駐だけする（自動起動で使う） |
| `snipjar.exe --quit` | 常駐を終了する |
| `snipjar.exe --capture-full` | 画面全体を撮って送る。結果は `%APPDATA%\snipjar\last-run.log` |

## アンインストール

```powershell
.\install.ps1 -Uninstall
```

exe・ショートカット・自動起動の登録を消す。設定とバケットと画像はそのまま残るので、
不要なら Cloudflare ダッシュボードから Worker とバケットを消す。

## 構成

| | |
|---|---|
| `worker/index.js` | アップロードの受け口・画像配信・一覧ページ (Workers + R2) |
| `client/*.cs` | 常駐アプリ (C# / WinForms, .NET Framework 4.8) |
| `setup.ps1` | デプロイから導入まで通しでやる。他人が使うときの入り口 |
| `build.ps1` | Windows 同梱の `csc.exe` でビルド。.NET SDK も NuGet も要らない |
| `install.ps1` | 配置・自動起動の登録・ショートカット作成 |
| `release.ps1` | 配布用 zip と SHA256SUMS を作る |

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
- **一覧のために別の索引を持たない**。R2 の `list()` は辞書順しか返さないので、キーの先頭に
  反転タイムスタンプを埋め、辞書順＝新しい順にした。ランダム部 16 文字が推測不能性を担う。
- **サムネイルはクライアントが作る**。Workers 単体では画像を縮小できず、原寸 PNG を並べると
  1 ページ数十 MB になるため。JPEG のほうが大きくなる小さい画像では送らず、原寸に戻す。
- **一覧のトークンは Cookie に入れる**。URL に載せると履歴や共有リンクに残る。
- **URL コピーは `execCommand` にフォールバックする**。`navigator.clipboard` は
  フォーカスと許可を要求し、埋め込みビューでは拒否されることがある。
- **シークレットは `secret bulk` で登録する**。PowerShell から `secret put` にパイプすると
  設定ファイルと一致しない値が入ることがあり、後から 401 として出てくる。

## ライセンス

[MIT](LICENSE)
