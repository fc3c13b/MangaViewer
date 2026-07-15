#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
img2img バッチ生成スクリプト
WebUI(Forge) の API を使って、元画像＋プロンプトからバリエーション画像を自動生成

使い方:
  1. inputs/ に元画像を配置
  2. prompts.txt にプロンプトを書く（または *: で共通プロンプト）
  3. config.json を編集（必要に応じて）
  4. python generate.py を実行
"""

import os
import sys
import json
import base64
import glob
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import requests


# ============================================================
# 設定読み込み
# ============================================================

def load_config(config_path: str = "config.json") -> dict:
    """設定ファイルを読み込む"""
    with open(config_path, "r", encoding="utf-8") as f:
        return json.load(f)


def load_prompts(prompts_path: str = "prompts.txt") -> Tuple[Dict[str, str], Optional[str]]:
    """
    プロンプトファイルを読み込む
    
    Returns:
        image_prompts: {画像名: プロンプト} の辞書
        global_prompt: 全画像共通プロンプト（設定されていれば）
    """
    image_prompts = {}
    global_prompt = None
    
    with open(prompts_path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            # 空行・コメントはスキップ
            if not line or line.startswith("#"):
                continue
            
            # ファイル名:プロンプト の形式をパース
            if ":" not in line:
                continue
            
            filename, prompt = line.split(":", 1)
            filename = filename.strip()
            prompt = prompt.strip()
            
            if filename == "*":
                global_prompt = prompt
            else:
                image_prompts[filename] = prompt
    
    return image_prompts, global_prompt


# ============================================================
# API コール（WebUI Forge img2img）
# ============================================================

def img2img_api(
    api_url: str,
    image_path: str,
    prompt: str,
    negative_prompt: str = "",
    steps: int = 30,
    cfg_scale: float = 7.5,
    width: int = 1024,
    height: int = 1024,
    denoising_strength: float = 0.7,
    seed: int = -1,
    sampler_name: str = "DPM++ 2M Karras",
    scheduler: str = "Karras",
) -> Optional[str]:
    """
    WebUI Forge の img2img API を呼び出し、生成画像の base64 データを返す
    
    Returns:
        base64 エンコードされた画像文字列（失敗時は None）
    """
    # 画像を base64 にエンコード
    with open(image_path, "rb") as f:
        image_b64 = base64.b64encode(f.read()).decode("utf-8")
    
    # img2img API ペイロード（Forge / stable-diffusion-webui 互換）
    payload = {
        "prompt": prompt,
        "negative_prompt": negative_prompt,
        "init_images": [image_b64],  # img2img の入力画像
        "steps": steps,
        "cfg_scale": cfg_scale,
        "width": width,
        "height": height,
        "denoising_strength": denoising_strength,
        "seed": seed if seed >= 0 else -1,
        "sampler_name": sampler_name,
        "scheduler": scheduler,
    }
    
    try:
        response = requests.post(
            f"{api_url}/sdapi/v1/img2img",
            json=payload,
            timeout=300,  # 5分タイムアウト（重い生成に余裕を持たせる）
        )
        response.raise_for_status()
        result = response.json()
        
        if "images" in result and len(result["images"]) > 0:
            return result["images"][0]
        else:
            print(f"  ⚠ 生成結果がありません（seed={seed}）")
            return None
            
    except requests.exceptions.ConnectionError:
        print(f"  ✗ API に接続できません。WebUI が起動しているか確認してください。（{api_url}）")
        return None
    except Exception as e:
        print(f"  ✗ エラー発生: {e}")
        return None


# ============================================================
# メイン処理
# ============================================================

def main():
    script_dir = Path(__file__).parent.resolve()
    
    # 設定読み込み
    config_path = script_dir / "config.json"
    prompts_path = script_dir / "prompts.txt"
    
    print("=" * 60)
    print("  AI マンガ画像バッチ生成ツール")
    print("=" * 60)
    print()
    
    config = load_config(str(config_path))
    image_prompts, global_prompt = load_prompts(str(prompts_path))
    
    # ディレクトリ設定
    inputs_dir = script_dir / "inputs"
    outputs_dir = script_dir / "outputs"
    outputs_dir.mkdir(exist_ok=True)
    
    if not inputs_dir.exists():
        print("エラー: inputs/ ディレクトリが見つかりません。")
        sys.exit(1)
    
    # 対象画像の探索（png, jpg, jpeg, webp）
    image_extensions = ["*.png", "*.jpg", "*.jpeg", "*.webp"]
    input_images = []
    for ext in image_extensions:
        input_images.extend(glob.glob(str(inputs_dir / ext)))
    
    if not input_images:
        print("エラー: inputs/ に画像が見つかりません。")
        sys.exit(1)
    
    # 設定の表示
    print(f"API URL       : {config['api_url']}")
    print(f"対象画像数     : {len(input_images)}")
    print(f"生成枚数/画像  : {config.get('samples_per_image', 3)}")
    print(f"解像度         : {config['width']}x{config['height']}")
    print(f"ステップ数      : {config['steps']}")
    print(f"Denoising     : {config['denoising_strength']}")
    print()
    
    # API 接続確認
    try:
        resp = requests.get(f"{config['api_url']}/sdapi/v1/options", timeout=5)
        if resp.status_code == 200:
            print("✓ WebUI に接続しました。")
        else:
            print(f"⚠ API のステータスが異常です（{resp.status_code}）。続行します…")
    except Exception as e:
        print(f"✗ WebUI に接続できません: {e}")
        print("  WebUI を起動して --api オプションを付けてください。")
        sys.exit(1)
    
    # 画像ごとに処理
    total_count = 0
    success_count = 0
    
    for img_path in sorted(input_images):
        filename = Path(img_path).name
        stem = Path(img_path).stem
        
        # プロンプトの決定（個別 > グローバル）
        prompt = image_prompts.get(filename, global_prompt)
        if not prompt:
            print(f"  ⚠ {filename}: プロンプトが見つからないためスキップ")
            continue
        
        samples = config.get("samples_per_image", 3)
        
        print(f"\n処理中: {filename} ({samples}枚生成)")
        print(f"  プロンプト: {prompt[:80]}{'...' if len(prompt) > 80 else ''}")
        
        for i in range(samples):
            total_count += 1
            
            # シードの決定（-1 でランダム、それ以外で固定）
            seed = config.get("seed", -1)
            if seed == -1:
                import random
                seed = random.randint(0, 2**32 - 1)
            
            result_b64 = img2img_api(
                api_url=config["api_url"],
                image_path=img_path,
                prompt=prompt,
                negative_prompt="low quality, worst quality, blurry, bad anatomy, extra fingers",
                steps=config.get("steps", 30),
                cfg_scale=config.get("cfg_scale", 7.5),
                width=config.get("width", 1024),
                height=config.get("height", 1024),
                denoising_strength=config.get("denoising_strength", 0.7),
                seed=seed,
                sampler_name=config.get("sampler_name", "DPM++ 2M Karras"),
                scheduler=config.get("scheduler", "Karras"),
            )
            
            if result_b64:
                # base64 → 画像ファイルとして保存
                img_data = base64.b64decode(result_b64)
                out_filename = f"{stem}_v{i+1}_s{seed}.png"
                out_path = outputs_dir / out_filename
                
                with open(out_path, "wb") as f:
                    f.write(img_data)
                
                print(f"  ✓ {out_filename} を保存しました。")
                success_count += 1
            else:
                print(f"  ✗ 生成に失敗しました（試行 {i+1}/{samples}）")
    
    # まとめ
    print()
    print("=" * 60)
    print(f"  完了! 合計 {total_count} 件中 {success_count} 件成功")
    print(f"  出力先: {outputs_dir}")
    print("=" * 60)


if __name__ == "__main__":
    main()