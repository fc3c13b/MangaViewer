# AI マンガ画像バッチ生成ツール

## 概要

- WebUI(Forge) の img2img API を使って、元画像＋プロンプトからバリエーション画像を自動生成
- ComfyUI なし・ノード設定なし・スクリプト+テキストファイルだけで動作

## スペック要件

- GPU: NVIDIA RTX 3060 以上推奨（RTX 4070 Ti Super 16GB で快適）
- RAM: 16GB 以上
- Python 3.10+ 

## セットアップ手順

### 1. WebUI(Forge) のインストール

```bash
git clone https://github.com/lllydviel/StableDiffusionWebuiForge.git
cd StableDiffusionWebuiForge
```

### 2. モデルの配置

- SDXL またはアニメ風モデル（例: Anything V5, Counterfeit-V3.0 など）をダウンロード
- `models/Stable-diffusion/` に `.ckpt` または `.safetensors` ファイルを配置

### 3. API 有効化で起動

```bash
# windows 用（venv 使用の場合）
.\venv\Scripts\python.exe launch.py --api
```

- ブラウザで `http://127.0.0.1:7860` にアクセス可能に

### 4. このディレクトリを使う準備

```bash
pip install requests Pillow
```

## 使い方

### ディレクトリ構成

```
image_gen/
├── inputs/          # 元画像を配置（スケッチ・参考画像など）
├── outputs/         # 生成結果がここに入る
├── prompts.txt      # プロンプト設定ファイル
├── config.json      # 生成パラメータ設定
└── generate.py      # バッチスクリプト（実行用）
```

### Step 1: 元画像を `inputs/` に配置

- 例: `inputs/chara_pose.png`（キャラクターのポーズ下書き）

### Step 2: `prompts.txt` を編集

```
# 各画像に対応するプロ_prompt（画像名:プロンプト の形式）
chara_pose.png:manga style, beautiful girl, dynamic pose, detailed background, masterpiece, best quality

# または全画像に共通のプロンプトを使いたい場合:
*:manga style, detailed illustration, high resolution
```

### Step 3: `config.json` を確認・編集

```json
{
  "api_url": "http://127.0.0.1:7860",
  "steps": 30,
  "cfg_scale": 7.5,
  "width": 1024,
  "height": 1024,
  "samples_per_image": 3,
  "denoising_strength": 0.7
}
```

### Step 4: スクリプトを実行

```bash
python generate.py
```

- `outputs/` に結果画像が保存される（各画像×バリエーション数分）

## Tips

- denoising_strength を高くすると元画像からの/deviation が大きくなる
- マンガのキャラ一貫性: 同じプロンプト＋同じモデル＋低 denoising で統一感を出せる
- ControlNet も API から利用可能（別途設定必要）